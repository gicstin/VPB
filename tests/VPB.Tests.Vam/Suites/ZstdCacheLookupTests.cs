using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class ZstdCacheLookupTests
    {
        public ZstdCacheLookupTests(VamFixture vam) { }

        [Fact]
        public void PrewarmedIndexPrefersExactFlagsAndSkipsDeletedOrIncompleteVariants()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_variants_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                string stem = "face_jpg_10_" + time;
                WriteCache(dir, stem + "_C");
                WriteCache(dir, stem + "_C_L");
                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);
                string exact = Path.Combine(dir, stem + "_C_L.zvamcache");
                string fallback = Path.Combine(dir, stem + "_C.zvamcache");
                Assert.Equal(exact, TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C_L"));

                TextureUtil.TryDeleteZstdCacheFile(exact);
                Assert.Equal(fallback, TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C_L"));

                File.Delete(fallback + "meta");
                Assert.Null(TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C_L"));

                WriteCache(dir, stem + "_C_L");
                TextureUtil.NoteZstdCacheFileWritten(exact);
                Assert.Equal(exact, TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C_L"));
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Theory]
        [InlineData("_C", "_C_L", "_C_A", "10", false)]
        [InlineData("_C", "_C_L", "_C_A", "10", true)]
        [InlineData("_C", "_C_L", "_C_A", "20", false)]
        [InlineData("_C", "_C_L", "_C_A", "20", true)]
        [InlineData("_C_L", "_C_A", "_C", "10", false)]
        [InlineData("_C_L", "_C_A", "_C", "10", true)]
        [InlineData("_C_L", "_C_A", "_C", "20", false)]
        [InlineData("_C_L", "_C_A", "_C", "20", true)]
        public void IndexedVariantsFollowFallbackSignatureOrder(string requested, string preferred, string lowerPriority,
            string storedSize, bool lowerPriorityFirst)
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_signature_order_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                string stem = "face_jpg_" + storedSize + "_" + time;
                WriteCache(dir, stem + preferred);
                WriteCache(dir, stem + lowerPriority);
                string preferredPath = Path.Combine(dir, stem + preferred + ".zvamcache");
                string lowerPriorityPath = Path.Combine(dir, stem + lowerPriority + ".zvamcache");
                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);

                var indexField = typeof(TextureUtil).GetField("s_ZstdDirIndexByNameTime",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(indexField);
                var index = (Dictionary<string, List<string>>)indexField.GetValue(null);
                List<string> entries = Assert.Single(index).Value;
                Assert.Equal(2, entries.Count);
                entries.Clear();
                entries.Add(lowerPriorityFirst ? lowerPriorityPath : preferredPath);
                entries.Add(lowerPriorityFirst ? preferredPath : lowerPriorityPath);

                Assert.Equal(preferredPath, TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, requested));
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void IncompleteIndexedVariantCannotHideAValidSizeFallback()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_incomplete_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                WriteCache(dir, "face_jpg_10_" + time + "_C");
                WriteCache(dir, "face_jpg_20_" + time + "_C");
                File.Delete(Path.Combine(dir, "face_jpg_10_" + time + "_C.zvamcachemeta"));
                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);
                Assert.Equal(Path.Combine(dir, "face_jpg_20_" + time + "_C.zvamcache"),
                    TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C"));
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void FullSizeIndexedCacheBeatsSameSizeTokenThumbnail()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_full_before_thumb_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                WriteCache(dir, "face_jpg_10_" + time + "_512_512_C");
                WriteCache(dir, "face_jpg_20_" + time + "_C");
                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);

                Assert.Equal(Path.Combine(dir, "face_jpg_20_" + time + "_C.zvamcache"),
                    TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C"));
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void PublishingIndexInvalidatesRevisionCapturedByAnEarlierLookup()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_publish_revision_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                WriteCache(dir, "face_jpg_20_" + time + "_C");
                var revision = typeof(TextureUtil).GetField("s_ZstdResolveRevision",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(revision);
                int capturedBeforeBuild = (int)revision.GetValue(null);

                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);

                Assert.NotEqual(capturedBeforeBuild, (int)revision.GetValue(null));
                Assert.Equal(Path.Combine(dir, "face_jpg_20_" + time + "_C.zvamcache"),
                    TextureUtil.ResolveZstdCacheFileInDirectory(dir, "face_jpg", "10", time, "_C"));
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void WriteCache(string dir, string baseName)
        {
            string path = Path.Combine(dir, baseName + ".zvamcache");
            File.WriteAllBytes(path, new byte[] { 1 });
            File.WriteAllText(path + "meta", "{}");
        }

        [Fact]
        public void PlainCompressedNameKeepsSizeAndTimeApartFromDimensions()
        {
            string reconstructed;
            string size;
            string time;
            string sig;
            bool ok = TextureUtil.TryParseNativeVamCacheFileName(
                "00025_png_8165532_133281164220000000_C",
                out reconstructed, out size, out time, out sig);

            Assert.True(ok, "A full-texture zstd name must parse, or the cache index drops every non-thumbnail file.");
            Assert.Equal("8165532", size);
            Assert.Equal("133281164220000000", time);
            Assert.Equal("_C", sig);
        }

        [Fact]
        public void GluedDimensionNameStillParsesSizeAndTime()
        {
            string reconstructed;
            string size;
            string time;
            string sig;
            bool ok = TextureUtil.TryParseNativeVamCacheFileName(
                "genitalsS_jpg_5688272_132048701520000000512_512_C_L",
                out reconstructed, out size, out time, out sig);

            Assert.True(ok, "A thumbnail whose dimensions are glued to the timestamp must still parse.");
            Assert.Equal("5688272", size);
            Assert.Equal("132048701520000000", time);
            Assert.Equal("512_512_C_L", sig);
        }

        [Fact]
        public void MismatchedPlainCompressedCacheIsIndexed()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_plain_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "132449831680000000";
                WriteCache(dir, "face_jpg_2012485_" + time + "_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "face_jpg", "6936330", time, "_C");

                Assert.True(found != null && found.IndexOf("2012485_" + time + "_C.zvamcache", StringComparison.Ordinal) >= 0,
                    "A full texture stored under the entry size must serve when the lookup uses the package size.");
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void WrongSizeThumbnailRequestFinds512FromTheIndex()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_thumbsize_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "134285665280000000";
                WriteCache(dir, "Lips_Layer_jpg_28071_" + time + "_512_512_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "Lips_Layer_jpg", "127800464", time, "_C");

                Assert.True(found != null && found.IndexOf("28071_" + time + "_512_512_C.zvamcache", StringComparison.Ordinal) >= 0,
                    "A clothing thumb stored at 512 under the entry size must serve when the request uses the package size and no dimensions.");
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void ThumbnailStoredAt512ServesWithoutScanningTheCacheDirectory()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_dim_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                WriteCache(dir, "Norma_Hair_jpg_17288_" + time + "_512_512_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "Norma_Hair_jpg", "17288", time, "_C");

                Assert.True(found != null && found.IndexOf("512_512_C.zvamcache", StringComparison.Ordinal) >= 0,
                    "A clothing thumbnail cached as 512x512 must still serve when the load request has no dimensions.");
                Assert.Equal(0, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void DimensionalRequestFindsUnderscoredThumbnailWithoutScanning()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_join_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "134285665280000000";
                WriteCache(dir, "Lips_Layer_jpg_28071_" + time + "_512_512_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "Lips_Layer_jpg", "28071", time, "512_512_C");

                Assert.True(found != null && found.IndexOf("_512_512_C.zvamcache", StringComparison.Ordinal) >= 0,
                    "A 512 thumbnail written with an underscore before the dimensions must hit on the first lookup, or every clothing thumb walks the whole zstd cache.");
                Assert.Equal(0, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void MismatchedSizeTokenIsResolvedFromOneDirectoryIndex()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_size_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "134285665280000000";
                WriteCache(dir, "Lips_Layer_jpg_28071_" + time + "_512_512_C");
                for (int i = 0; i < 40; i++)
                    WriteCache(dir, "Decoy_jpg_" + i + "_11111111111111111" + (i % 10) + "_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "Lips_Layer_jpg", "127800464", time, "512_512_C");
                string again = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "Lips_Layer_jpg", "127800464", time, "512_512_C");

                Assert.True(found != null && found.IndexOf("28071_" + time, StringComparison.Ordinal) >= 0,
                    "A package-size lookup must still find the entry-size zstd cache, or scene loads fall back to raw jpg.");
                Assert.Equal(found, again);
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void CacheWrittenAfterIndexBuildServesOnceAnnounced()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_written_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "132449812780000000";
                WriteCache(dir, "Unrelated_jpg_5_" + time + "_C");

                string before = TextureUtil.ResolveZstdCacheFileInDirectory(dir, "cheer3_jpg", "519666", time, "_C");
                Assert.True(string.IsNullOrEmpty(before), "Precondition: nothing cached yet for the texture.");
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);

                WriteCache(dir, "cheer3_jpg_519666_" + time + "_C");
                TextureUtil.NoteZstdCacheFileWritten(Path.Combine(dir, "cheer3_jpg_519666_" + time + "_C.zvamcache"));

                string after = TextureUtil.ResolveZstdCacheFileInDirectory(dir, "cheer3_jpg", "519666", time, "_C");
                Assert.True(after != null && after.IndexOf("cheer3_jpg_519666_" + time + "_C.zvamcache", StringComparison.Ordinal) >= 0,
                    "A texture compressed during the session must serve from zstd on its next load instead of decoding the jpg again.");
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void IndexedDirectoryAnswersMissesWithoutProbingDisk()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_authority_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "134030888840000000";
                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);

                WriteCache(dir, "Tight_Pony_Base_jpg_480740_" + time + "_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(dir, "Tight_Pony_Base_jpg", "480740", time, "_C");

                Assert.True(string.IsNullOrEmpty(found),
                    "Once the zstd directory is indexed, misses must be answered from memory; under Wine every disk miss rescans the whole cache folder and stalls each texture load.");
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void PrewarmedIndexStillServesExistingCacheByPackageSize()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_prewarm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133925029340000000";
                WriteCache(dir, "long10_extra_jpg_91234_" + time + "_512_512_C");
                TextureUtil.PrewarmZstdCacheDirectoryIndex(dir);

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(dir, "long10_extra_jpg", "18665231", time, "_C");

                Assert.True(found != null && found.IndexOf("91234_" + time + "_512_512_C.zvamcache", StringComparison.Ordinal) >= 0,
                    "A hair thumbnail requested with the package size must still serve from a prewarmed index.");
                Assert.Equal(1, TextureUtil.ZstdCacheDirectoryIndexBuilds);
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void AlphaRequestDoesNotServeAPlainCompressedThumbnail()
        {
            TextureUtil.ResetZstdCacheDirectoryIndex();
            string dir = Path.Combine(Path.GetTempPath(), "vpb_zstd_alpha_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string time = "133881638589648118";
                WriteCache(dir, "face_jpg_10_" + time + "_C");
                WriteCache(dir, "face_jpg_10_" + time + "_512_512_C");

                string found = TextureUtil.ResolveZstdCacheFileInDirectory(
                    dir, "face_jpg", "10", time, "_C_A");

                Assert.True(string.IsNullOrEmpty(found),
                    "An alpha texture must not display the plain compressed cache, or eyelashes and decals lose transparency.");
            }
            finally
            {
                TextureUtil.ResetZstdCacheDirectoryIndex();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
