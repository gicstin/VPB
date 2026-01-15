using System;
using UnityEngine;
using System.Runtime.InteropServices;

namespace VPB
{
    public static class TextureUtil
    {
        public static int GetExpectedRawDataSize(int w, int h, TextureFormat fmt)
        {
            switch (fmt)
            {
                case TextureFormat.Alpha8: return w * h;
                case TextureFormat.RGB24: return w * h * 3;
                case TextureFormat.RGBA32: return w * h * 4;
                case TextureFormat.ARGB32: return w * h * 4;
                case TextureFormat.DXT1: return (Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4)) * 8;
                case TextureFormat.DXT5: return (Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4)) * 16;
                default: return 0;
            }
        }

        /// <summary>
        /// Loads raw texture data from a byte array using zero-copy IntPtr if the buffer is oversized.
        /// </summary>
        public static void SafeLoadRawTextureData(Texture2D t, byte[] data, int length, int w, int h, TextureFormat fmt)
        {
            if (t == null || data == null) return;
            
            int expected = GetExpectedRawDataSize(w, h, fmt);
            if (expected <= 0)
            {
                // Fallback for formats we don't have expected size for
                t.LoadRawTextureData(data);
                return;
            }

            if (length < expected)
            {
                LogUtil.LogWarning($"[VPB] SafeLoadRawTextureData: data length ({length}) is smaller than expected ({expected}) for {w}x{h} {fmt}");
                // We still try it, Unity might throw or it might work if Mips are involved but we don't handle that here.
                t.LoadRawTextureData(data);
                return;
            }

            if (data.Length == expected && length == expected)
            {
                t.LoadRawTextureData(data);
            }
            else
            {
                // Use IntPtr overload to avoid copying if the array is too large (pooled buffer)
                GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    t.LoadRawTextureData(pin.AddrOfPinnedObject(), expected);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"[VPB] SafeLoadRawTextureData (IntPtr) failed: {ex.Message}");
                    // Last resort fallback
                    t.LoadRawTextureData(data);
                }
                finally
                {
                    pin.Free();
                }
            }
        }

        /// <summary>
        /// Overload that uses data.Length as the valid data length.
        /// </summary>
        public static void SafeLoadRawTextureData(Texture2D t, byte[] data, int w, int h, TextureFormat fmt)
        {
            SafeLoadRawTextureData(t, data, data != null ? data.Length : 0, w, h, fmt);
        }
    }
}
