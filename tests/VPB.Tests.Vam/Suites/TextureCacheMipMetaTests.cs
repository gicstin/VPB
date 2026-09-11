using System.IO;
using SimpleJSON;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class TextureCacheMipMetaTests
    {
        private readonly ITestOutputHelper _out;
        public TextureCacheMipMetaTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static JSONNode BuildMeta(int w, int h, TextureFormat fmt)
        {
            JSONNode meta = new JSONClass();
            meta["width"].AsInt = w;
            meta["height"].AsInt = h;
            meta["format"] = fmt.ToString();
            return meta;
        }

        [Theory]
        [InlineData(TextureFormat.DXT1)]
        [InlineData(TextureFormat.DXT5)]
        public void ABaseOnlyBlockCompressedPayloadIsNeverRecordedAsMipped(TextureFormat fmt)
        {
            const int w = 1024, h = 1024;
            int baseSize = TextureUtil.GetExpectedRawDataSize(w, h, fmt);
            JSONNode meta = BuildMeta(w, h, fmt);

            TextureUtil.WriteMipFieldsToMeta(meta, w, h, fmt, baseSize, true);

            _out.WriteLine(fmt + " base=" + baseSize + " -> createMipMaps=" + meta["createMipMaps"].AsBool +
                           " mipStorage=" + meta["mipStorage"].Value);

            Assert.False(meta["createMipMaps"].AsBool,
                "A base-only " + fmt + " payload holds no mips and cannot grow them at runtime, so the queue's " +
                "request flag must not be recorded as fact.");
        }

        [Theory]
        [InlineData(TextureFormat.DXT1)]
        [InlineData(TextureFormat.DXT5)]
        [InlineData(TextureFormat.RGBA32)]
        public void EveryMetaTheWritersProduceIsStampableAsCurrent(TextureFormat fmt)
        {
            const int w = 512, h = 512;
            int[] lengths =
            {
                TextureUtil.GetExpectedRawDataSize(w, h, fmt),
                TextureUtil.GetExpectedFullMipChainSize(w, h, fmt)
            };

            foreach (int rawLength in lengths)
            {
                foreach (bool queueWantsMips in new[] { false, true })
                {
                    JSONNode meta = BuildMeta(w, h, fmt);
                    TextureUtil.WriteMipFieldsToMeta(meta, w, h, fmt, rawLength, queueWantsMips);

                    Assert.True(
                        TextureUtil.IsMipPayloadConsistent(
                            meta["createMipMaps"].AsBool, meta["mipStorage"].Value, rawLength, w, h, fmt),
                        fmt + " raw=" + rawLength + " queueMips=" + queueWantsMips +
                        " produced a meta the bulk compressor refuses to stamp with vpbVer, which the serve " +
                        "path then purges on first read.");
                }
            }
        }

        [Fact]
        public void AFullMipChainPayloadStillRecordsItsMips()
        {
            const int w = 512, h = 512;
            const TextureFormat fmt = TextureFormat.DXT5;
            int fullSize = TextureUtil.GetExpectedFullMipChainSize(w, h, fmt);
            JSONNode meta = BuildMeta(w, h, fmt);

            TextureUtil.WriteMipFieldsToMeta(meta, w, h, fmt, fullSize, true);

            Assert.True(meta["createMipMaps"].AsBool);
            Assert.Equal(TextureUtil.MipStorageFull, meta["mipStorage"].Value);
        }

        [Fact]
        public void ALegacyMippedBaseOnlyEntryIsRepairedInPlaceRatherThanDiscarded()
        {
            using (var install = new TempInstall("texcache_repair"))
            {
                const int w = 256, h = 256;
                const TextureFormat fmt = TextureFormat.DXT1;

                string cachePath = install.PathTo("Cache", "VPB", "legacy_jpg_1_1_C.zvamcache");
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                File.WriteAllBytes(cachePath, new byte[16]);

                JSONNode meta = BuildMeta(w, h, fmt);
                meta["createMipMaps"].AsBool = true;
                meta["mipCount"].AsInt = TextureUtil.CountMipLevels(w, h);
                meta["mipStorage"] = TextureUtil.MipStorageBase;
                File.WriteAllText(cachePath + "meta", meta.ToString());

                Assert.True(TextureUtil.CacheEntryNeedsSourceRebuild(cachePath),
                    "Fixture did not reproduce the legacy pre-v4 shape.");

                Assert.True(TextureUtil.TryRepairStaleMipMetaInPlace(cachePath));

                Assert.True(File.Exists(cachePath), "The payload was valid and must survive the repair.");
                Assert.False(TextureUtil.CacheEntryNeedsSourceRebuild(cachePath),
                    "Repair must terminate: a repaired entry that still reads as stale rebuilds forever.");

                JSONNode repaired = JSON.Parse(File.ReadAllText(cachePath + "meta"));
                Assert.False(repaired["createMipMaps"].AsBool);
                Assert.Equal(TextureUtil.TextureCacheVersion, repaired["vpbVer"].AsInt);
            }
        }
    }
}
