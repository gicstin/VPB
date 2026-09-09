using System;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ZstdTests
    {
        private readonly ITestOutputHelper _out;
        public ZstdTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static byte[] Compressible(int size)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = (byte)(i % 61);
            return data;
        }

        private static byte[] Incompressible(int size)
        {
            var data = new byte[size];
            uint state = 0x9E3779B9;
            for (int i = 0; i < size; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                data[i] = (byte)state;
            }
            return data;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(9)]
        [InlineData(19)]
        public void CompressibleDataSurvivesARoundTripAtEveryLevel(int level)
        {
            byte[] original = Compressible(64 * 1024);

            byte[] packed = ZstdCompressor.Compress(original, level);
            Assert.True(packed != null && packed.Length > 0,
                "Zstd produced no output at level " + level + ". If libzstd.dll and zstd.exe are both missing " +
                "from the test output directory, the csproj copy of the plugin's native payload did not run.");

            byte[] restored = ZstdCompressor.Decompress(packed);

            _out.WriteLine("level " + level + ": " + original.Length + " -> " + packed.Length +
                           " (" + (100.0 * packed.Length / original.Length).ToString("0.0") + "%)");

            Assert.Equal(original, restored);
            Assert.True(packed.Length < original.Length,
                "Compressible input did not get smaller at level " + level + ".");
        }

        [Fact]
        public void IncompressibleDataStillRoundTrips()
        {
            byte[] original = Incompressible(32 * 1024);
            byte[] restored = ZstdCompressor.Decompress(ZstdCompressor.Compress(original, 3));
            Assert.Equal(original, restored);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(15)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(1025)]
        [InlineData(65535)]
        [InlineData(65536)]
        public void BufferBoundariesRoundTrip(int size)
        {
            byte[] original = Compressible(size);
            byte[] restored = ZstdCompressor.Decompress(ZstdCompressor.Compress(original, 3));
            Assert.Equal(original, restored);
        }

        [Fact]
        public void EmptyAndNullInputAreHandledWithoutThrowing()
        {
            Assert.Equal(new byte[0], ZstdCompressor.Compress(null, 3));
            Assert.Equal(new byte[0], ZstdCompressor.Compress(new byte[0], 3));
        }

        [Fact]
        public void PartialLengthCompressesOnlyTheRequestedPrefix()
        {
            byte[] pooled = Compressible(8192);
            const int used = 4096;

            byte[] packed = ZstdCompressor.Compress(pooled, 3, used);
            byte[] restored = ZstdCompressor.Decompress(packed);

            Assert.Equal(used, restored.Length);
            for (int i = 0; i < used; i++) Assert.Equal(pooled[i], restored[i]);
        }

        [Fact]
        public void CacheFileRoundTripsThroughDisk()
        {
            using (var install = new TempInstall("zstd_cache"))
            {
                string path = install.PathTo("Cache", "VPB", "probe.zst");
                byte[] original = Compressible(48 * 1024);

                ZstdCompressor.SaveCache(path, original, 3);
                Assert.True(File.Exists(path), "SaveCache wrote nothing to " + path);

                byte[] restored = ZstdCompressor.LoadCache(path);
                Assert.Equal(original, restored);
            }
        }

        [Fact]
        public void LoadCacheOnAMissingFileReturnsNullRatherThanThrowing()
        {
            using (var install = new TempInstall("zstd_missing"))
            {
                Assert.Null(ZstdCompressor.LoadCache(install.PathTo("Cache", "VPB", "absent.zst")));
            }
        }

        [Fact]
        public void ATruncatedCacheFileNeverYieldsPartialData()
        {
            using (var install = new TempInstall("zstd_truncated"))
            {
                string path = install.PathTo("Cache", "VPB", "broken.zst");
                byte[] original = Compressible(16 * 1024);
                byte[] packed = ZstdCompressor.Compress(original, 3);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, Trim(packed, packed.Length / 2));

                byte[] restored = null;
                Exception thrown = Record.Exception(() => restored = ZstdCompressor.LoadCache(path));

                _out.WriteLine("truncated read: " +
                    (thrown != null ? "threw " + HeadlessVam.DescribeUnwrapped(thrown)
                                    : "returned " + (restored == null ? "null" : restored.Length + " bytes")));
                _out.WriteLine("LoadCache currently has no callers in src/; the live read path is");
                _out.WriteLine("NativeTextureOnDemandCache. Throwing is acceptable here, silently returning a");
                _out.WriteLine("short buffer is not - that is what renders a texture as garbage instead of");
                _out.WriteLine("rebuilding the cache entry.");

                Assert.True(thrown != null || restored == null || restored.Length == 0 || restored.Length == original.Length,
                    "A half-written cache file produced " + (restored == null ? "null" : restored.Length + " bytes") +
                    " where the original was " + original.Length + ". Partial data returned as success is the " +
                    "failure mode this guards.");
            }
        }

        [Fact]
        public void GarbageInputIsRejectedRatherThanReturningNonsense()
        {
            byte[] garbage = Encoding.ASCII.GetBytes("this is not a zstd frame at all, not even close");

            byte[] restored = null;
            Exception thrown = Record.Exception(() => restored = ZstdCompressor.Decompress(garbage));

            _out.WriteLine("thrown: " + (thrown == null ? "(none)" : thrown.GetType().Name));
            Assert.True(thrown != null || restored == null || restored.Length == 0,
                "Decompressing garbage returned " + (restored == null ? "null" : restored.Length + " bytes") +
                " without an error. Silently returning a wrong buffer is how a corrupt texture cache renders as " +
                "garbage instead of being rebuilt.");
        }

        private static byte[] Trim(byte[] source, int length)
        {
            var trimmed = new byte[length];
            Buffer.BlockCopy(source, 0, trimmed, 0, length);
            return trimmed;
        }
    }
}
