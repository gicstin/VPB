using System;
using UnityEngine;

namespace VPB.Tests.Runtime
{
    [VpbRuntimeSuite(Name = "TextureCache", Order = 10)]
    public static class TextureCacheSuite
    {
        private const int Width = 64;
        private const int Height = 64;

        private static byte[] Rgba32Payload(int w, int h, byte seed)
        {
            var data = new byte[w * h * 4];
            for (int i = 0; i < data.Length; i += 4)
            {
                data[i] = (byte)((i / 4 + seed) & 0xFF);
                data[i + 1] = seed;
                data[i + 2] = (byte)(255 - seed);
                data[i + 3] = 255;
            }
            return data;
        }

        private static Texture2D NewTexture(int w, int h, bool mipChain)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, mipChain, false);
        }

        [VpbRuntimeTest]
        public static void ACorrectlySizedPayloadIsAppliedAndReadableBack()
        {
            Texture2D tex = NewTexture(Width, Height, false);
            try
            {
                byte[] payload = Rgba32Payload(Width, Height, 7);

                bool ok = TextureUtil.ApplyCachedRawToTexture(
                    tex, payload, Width, Height, TextureFormat.RGBA32,
                    createMipMaps: false, linear: false, markNonReadable: false, forceReadable: true);

                RuntimeAssert.True(ok, "ApplyCachedRawToTexture refused a correctly sized RGBA32 payload.");

                Color32 first = tex.GetPixels32()[0];
                RuntimeAssert.Equal((byte)7, first.g,
                    "The applied payload did not reach the texture's CPU pixels. Graphics.CopyTexture silently " +
                    "no-ops on a layout mismatch, so a blind 'true' here is how a cached texture renders as garbage.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [VpbRuntimeTest]
        public static void AShortPayloadIsRefusedRatherThanPartiallyApplied()
        {
            Texture2D tex = NewTexture(Width, Height, false);
            try
            {
                byte[] full = Rgba32Payload(Width, Height, 3);
                var truncated = new byte[full.Length / 2];
                Buffer.BlockCopy(full, 0, truncated, 0, truncated.Length);

                bool ok = TextureUtil.ApplyCachedRawToTexture(
                    tex, truncated, Width, Height, TextureFormat.RGBA32,
                    createMipMaps: false, linear: false, markNonReadable: false, forceReadable: true);

                RuntimeAssert.False(ok,
                    "A half-length payload was accepted. A truncated cache entry must be rejected so the caller " +
                    "rebuilds it, not uploaded as a half-filled texture.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [VpbRuntimeTest]
        public static void AMismatchedSizeIsRejectedOrResizedNeverSilentlyIgnored()
        {
            Texture2D tex = NewTexture(Width, Height, false);
            try
            {
                const int otherW = Width * 2;
                const int otherH = Height * 2;
                byte[] payload = Rgba32Payload(otherW, otherH, 11);

                bool ok = TextureUtil.ApplyCachedRawToTexture(
                    tex, payload, otherW, otherH, TextureFormat.RGBA32,
                    createMipMaps: false, linear: false, markNonReadable: false, forceReadable: true);

                if (!ok) return;

                RuntimeAssert.Equal(otherW, tex.width,
                    "ApplyCachedRawToTexture returned true for a differently sized payload but left the texture at " +
                    "its old width. Either resize and verify, or return false - never report success without one.");
                RuntimeAssert.Equal(otherH, tex.height,
                    "Same as above for height.");

                Color32 first = tex.GetPixels32()[0];
                RuntimeAssert.Equal((byte)11, first.g,
                    "The texture was resized but the pixels were never filled.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [VpbRuntimeTest]
        public static void NullAndEmptyInputsAreRefused()
        {
            Texture2D tex = NewTexture(Width, Height, false);
            try
            {
                RuntimeAssert.False(
                    TextureUtil.ApplyCachedRawToTexture(null, Rgba32Payload(Width, Height, 1), Width, Height,
                        TextureFormat.RGBA32, false, false, false, true),
                    "A null texture must be refused.");

                RuntimeAssert.False(
                    TextureUtil.ApplyCachedRawToTexture(tex, null, Width, Height,
                        TextureFormat.RGBA32, false, false, false, true),
                    "A null payload must be refused.");

                RuntimeAssert.False(
                    TextureUtil.ApplyCachedRawToTexture(tex, new byte[0], Width, Height,
                        TextureFormat.RGBA32, false, false, false, true),
                    "An empty payload must be refused.");

                RuntimeAssert.False(
                    TextureUtil.ApplyCachedRawToTexture(tex, Rgba32Payload(Width, Height, 1), 0, Height,
                        TextureFormat.RGBA32, false, false, false, true),
                    "A zero width must be refused.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [VpbRuntimeTest]
        public static void AMipChainCannotBeGrownAfterConstruction()
        {
            Texture2D noMips = NewTexture(Width, Height, false);
            Texture2D withMips = NewTexture(Width, Height, true);
            try
            {
                RuntimeAssert.Equal(1, noMips.mipmapCount,
                    "A texture constructed with mipChain:false must have exactly one level.");

                RuntimeAssert.True(withMips.mipmapCount > 1,
                    "A texture constructed with mipChain:true must have a real chain.");

                noMips.Apply(true, false);
                RuntimeAssert.Equal(1, noMips.mipmapCount,
                    "Apply(updateMipmaps:true) appeared to add mip levels to a texture that was allocated without " +
                    "a chain. It cannot: the chain must be requested in the constructor, from the caller's intent " +
                    "rather than from the payload size.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(noMips);
                UnityEngine.Object.DestroyImmediate(withMips);
            }
        }

        [VpbRuntimeTest]
        public static void ExpectedRawSizeMatchesUnityForCommonFormats()
        {
            AssertExpectedSize(TextureFormat.RGBA32, 4);
            AssertExpectedSize(TextureFormat.RGB24, 3);
            AssertExpectedSize(TextureFormat.Alpha8, 1);
        }

        private static void AssertExpectedSize(TextureFormat format, int bytesPerPixel)
        {
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(Width, Height, format, false, false);
                byte[] raw = tex.GetRawTextureData();
                RuntimeAssert.Equal(Width * Height * bytesPerPixel, raw.Length,
                    "Unity's raw buffer for " + format + " is not " + bytesPerPixel + " bytes per pixel on this " +
                    "build, so the cache's size checks would let a wrong-sized payload through.");
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
