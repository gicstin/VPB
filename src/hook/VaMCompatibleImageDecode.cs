using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VPB
{
    /// <summary>VaM stock GDI+ decode from <c>ref/vam</c> ImageLoaderThreaded.ProcessFromStream — alpha/TIF parity.</summary>
    internal static class VaMCompatibleImageDecode
    {
        public static bool ShouldUseVaMDecode(bool isThumbnail, bool onDemandDecodeFromSource, string imgPath)
        {
            if (onDemandDecodeFromSource) return true;
            if (!isThumbnail) return true;
            return TextureUtil.IsTiffTexturePath(imgPath);
        }

        public static bool TryDecodeFromStream(
            Stream st,
            bool setSize,
            int setWidth,
            int setHeight,
            bool fillBackground,
            bool compress,
            bool isNormalMap,
            bool createAlphaFromGrayscale,
            bool createNormalFromBump,
            float bumpStrength,
            bool invert,
            out int width,
            out int height,
            out TextureFormat textureFormat,
            out byte[] raw,
            out int rawLength,
            out string error)
        {
            width = 0;
            height = 0;
            textureFormat = TextureFormat.RGB24;
            raw = null;
            rawLength = 0;
            error = null;

            Bitmap bitmap = null;
            Bitmap bitmap2 = null;
            SolidBrush solidBrush = null;
            System.Drawing.Graphics graphics = null;

            try
            {
                bitmap = new Bitmap(st);
                solidBrush = new SolidBrush(System.Drawing.Color.White);
                bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);

                if (!setSize)
                {
                    width = bitmap.Width;
                    height = bitmap.Height;
                    if (compress)
                    {
                        int num = width / 4;
                        if (num == 0) num = 1;
                        width = num * 4;
                        int num2 = height / 4;
                        if (num2 == 0) num2 = 1;
                        height = num2 * 4;
                    }
                }
                else
                {
                    width = setWidth;
                    height = setHeight;
                }

                int bytesPerPixel = 3;
                textureFormat = TextureFormat.RGB24;
                PixelFormat format = PixelFormat.Format24bppRgb;
                if (createAlphaFromGrayscale || isNormalMap || createNormalFromBump || bitmap.PixelFormat == PixelFormat.Format32bppArgb)
                {
                    textureFormat = TextureFormat.RGBA32;
                    format = PixelFormat.Format32bppArgb;
                    bytesPerPixel = 4;
                }

                bitmap2 = new Bitmap(width, height, format);
                graphics = System.Drawing.Graphics.FromImage(bitmap2);
                Rectangle rect = new Rectangle(0, 0, width, height);
                if (setSize)
                {
                    if (fillBackground)
                    {
                        graphics.FillRectangle(solidBrush, rect);
                    }
                    float scale = Mathf.Min((float)width / bitmap.Width, (float)height / bitmap.Height);
                    int drawW = (int)(bitmap.Width * scale);
                    int drawH = (int)(bitmap.Height * scale);
                    graphics.DrawImage(bitmap, (width - drawW) / 2, (height - drawH) / 2, drawW, drawH);
                }
                else
                {
                    graphics.DrawImage(bitmap, 0, 0, width, height);
                }

                BitmapData bitmapData = bitmap2.LockBits(rect, ImageLockMode.ReadOnly, bitmap2.PixelFormat);
                int pixelCount = width * height;
                int dataSize = pixelCount * bytesPerPixel;
                int allocSize = Mathf.CeilToInt((float)dataSize * 1.5f);
                raw = new byte[allocSize];
                Marshal.Copy(bitmapData.Scan0, raw, 0, dataSize);
                bitmap2.UnlockBits(bitmapData);
                rawLength = dataSize;

                bool normalWithAlpha = isNormalMap && bytesPerPixel == 4;
                for (int i = 0; i < dataSize; i += bytesPerPixel)
                {
                    byte b = raw[i];
                    raw[i] = raw[i + 2];
                    raw[i + 2] = b;
                    if (normalWithAlpha)
                    {
                        raw[i + 3] = byte.MaxValue;
                    }
                }

                if (invert)
                {
                    for (int j = 0; j < dataSize; j++)
                    {
                        raw[j] = (byte)(255 - raw[j]);
                    }
                }

                if (createAlphaFromGrayscale)
                {
                    for (int k = 0; k < dataSize; k += 4)
                    {
                        int r = raw[k];
                        int g = raw[k + 1];
                        int b2 = raw[k + 2];
                        raw[k + 3] = (byte)((r + g + b2) / 3);
                    }
                }

                if (createNormalFromBump)
                {
                    byte[] bumpOut = new byte[dataSize * 2];
                    float[][] heights = new float[height][];
                    for (int row = 0; row < height; row++)
                    {
                        heights[row] = new float[width];
                        for (int col = 0; col < width; col++)
                        {
                            int idx = (row * width + col) * 4;
                            float h = (raw[idx] + raw[idx + 1] + raw[idx + 2]) / 768f;
                            heights[row][col] = h;
                        }
                    }

                    Vector3 n = default(Vector3);
                    for (int row = 0; row < height; row++)
                    {
                        for (int col = 0; col < width; col++)
                        {
                            float tl = 0.5f, l = 0.5f, bl = 0.5f, t = 0.5f, b = 0.5f, tr = 0.5f, r = 0.5f, br = 0.5f;
                            int left = col - 1;
                            int right = col + 1;
                            int up = row + 1;
                            int down = row - 1;

                            if (up >= 0 && up < height && left >= 0 && left < width) tl = heights[up][left];
                            if (left >= 0 && left < width) l = heights[row][left];
                            if (down >= 0 && down < height && left >= 0 && left < width) bl = heights[down][left];
                            if (up >= 0 && up < height) t = heights[up][col];
                            if (down >= 0 && down < height) b = heights[down][col];
                            if (up >= 0 && up < height && right >= 0 && right < width) tr = heights[up][right];
                            if (right >= 0 && right < width) r = heights[row][right];
                            if (down >= 0 && down < height && right >= 0 && right < width) br = heights[down][right];

                            float dx = tr + 2f * r + br - tl - 2f * l - bl;
                            float dy = bl + 2f * b + br - tl - 2f * t - tr;
                            n.x = dx * bumpStrength;
                            n.y = dy * bumpStrength;
                            n.z = 1f;
                            n.Normalize();
                            int outIdx = (row * width + col) * 4;
                            bumpOut[outIdx] = (byte)((n.x * 0.5f + 0.5f) * 255f);
                            bumpOut[outIdx + 1] = (byte)((n.y * 0.5f + 0.5f) * 255f);
                            bumpOut[outIdx + 2] = (byte)((n.z * 0.5f + 0.5f) * 255f);
                            bumpOut[outIdx + 3] = byte.MaxValue;
                        }
                    }
                    raw = bumpOut;
                    rawLength = bumpOut.Length;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (solidBrush != null) try { solidBrush.Dispose(); } catch { }
                if (graphics != null) try { graphics.Dispose(); } catch { }
                if (bitmap != null) try { bitmap.Dispose(); } catch { }
                if (bitmap2 != null) try { bitmap2.Dispose(); } catch { }
            }
        }
    }
}
