using System;
using System.Runtime.InteropServices;

namespace var_browser
{
    public static class ImageProcessingOptimization
    {
        public static void FastBitmapCopy(byte[] srcData, int srcWidth, int srcHeight, int srcStride, int srcBpp,
                                          byte[] dstData, int dstWidth, int dstHeight, int dstStride, int dstBpp,
                                          bool centered, bool fillWhiteBackground)
        {
            if (srcBpp != 4 && srcBpp != 3)
                throw new ArgumentException("srcBpp must be 3 or 4");
            if (dstBpp != 4 && dstBpp != 3)
                throw new ArgumentException("dstBpp must be 3 or 4");

            if (!centered)
            {
                FastBitmapCopyUnscaled(srcData, srcWidth, srcHeight, srcStride, srcBpp,
                                       dstData, dstWidth, dstHeight, dstStride, dstBpp);
            }
            else
            {
                FastBitmapCopyScaled(srcData, srcWidth, srcHeight, srcStride, srcBpp,
                                     dstData, dstWidth, dstHeight, dstStride, dstBpp, fillWhiteBackground);
            }
        }

        private static void FastBitmapCopyUnscaled(byte[] srcData, int srcWidth, int srcHeight, int srcStride, int srcBpp,
                                                    byte[] dstData, int dstWidth, int dstHeight, int dstStride, int dstBpp)
        {
            int copyWidth = Math.Min(srcWidth, dstWidth);
            int copyHeight = Math.Min(srcHeight, dstHeight);
            int copyBytes = copyWidth * srcBpp;

            for (int y = 0; y < copyHeight; y++)
            {
                Buffer.BlockCopy(srcData, y * srcStride, dstData, y * dstStride, copyBytes);
            }
        }

        private static void FastBitmapCopyScaled(byte[] srcData, int srcWidth, int srcHeight, int srcStride, int srcBpp,
                                                  byte[] dstData, int dstWidth, int dstHeight, int dstStride, int dstBpp,
                                                  bool fillWhiteBackground)
        {
            if (fillWhiteBackground)
            {
                Array.Clear(dstData, 0, dstData.Length);
                if (dstBpp == 4)
                {
                    for (int i = 3; i < dstData.Length; i += 4)
                        dstData[i] = 255;
                }
            }

            float scaleX = (float)srcWidth / dstWidth;
            float scaleY = (float)srcHeight / dstHeight;
            float scale = Math.Min(scaleX, scaleY);

            int scaledWidth = (int)(srcWidth / scale);
            int scaledHeight = (int)(srcHeight / scale);
            int offsetX = (dstWidth - scaledWidth) / 2;
            int offsetY = (dstHeight - scaledHeight) / 2;

            for (int dy = 0; dy < scaledHeight && dy + offsetY < dstHeight; dy++)
            {
                float sy = dy * scale;
                int srcY = (int)sy;
                if (srcY >= srcHeight) srcY = srcHeight - 1;

                int dstRowIdx = (dy + offsetY) * dstStride;

                for (int dx = 0; dx < scaledWidth && dx + offsetX < dstWidth; dx++)
                {
                    float sx = dx * scale;
                    int srcX = (int)sx;
                    if (srcX >= srcWidth) srcX = srcWidth - 1;

                    int srcIdx = srcY * srcStride + srcX * srcBpp;
                    int dstIdx = dstRowIdx + (dx + offsetX) * dstBpp;

                    dstData[dstIdx] = srcData[srcIdx];
                    dstData[dstIdx + 1] = srcData[srcIdx + 1];
                    dstData[dstIdx + 2] = srcData[srcIdx + 2];
                    if (dstBpp == 4)
                        dstData[dstIdx + 3] = (srcBpp == 4) ? srcData[srcIdx + 3] : (byte)255;
                }
            }
        }

        public static unsafe void OptimizedNormalMapGeneration(byte[] raw, int width, int height, float bumpStrength)
        {
            if (raw == null || width < 3 || height < 3)
                return;

            int stride = width * 4;
            fixed (byte* ptr = raw)
            {
                ProcessNormalMapFast(ptr, width, height, stride, bumpStrength);
            }
        }

        private static unsafe void ProcessNormalMapFast(byte* data, int width, int height, int stride, float bumpStrength)
        {
            byte* pDst = data;
            int innerWidth = width - 2;
            int innerHeight = height - 2;

            for (int y = 1; y < height - 1; y++)
            {
                byte* pRow = data + y * stride;

                for (int x = 1; x < width - 1; x++)
                {
                    byte* p = pRow + x * 4;

                    byte* pTL = data + (y - 1) * stride + (x - 1) * 4;
                    byte* pTM = data + (y - 1) * stride + x * 4;
                    byte* pTR = data + (y - 1) * stride + (x + 1) * 4;
                    byte* pML = pRow + (x - 1) * 4;
                    byte* pMR = pRow + (x + 1) * 4;
                    byte* pBL = data + (y + 1) * stride + (x - 1) * 4;
                    byte* pBM = data + (y + 1) * stride + x * 4;
                    byte* pBR = data + (y + 1) * stride + (x + 1) * 4;

                    float tl = (*pTL + *(pTL + 1) + *(pTL + 2)) * (1.0f / 768.0f);
                    float tm = (*pTM + *(pTM + 1) + *(pTM + 2)) * (1.0f / 768.0f);
                    float tr = (*pTR + *(pTR + 1) + *(pTR + 2)) * (1.0f / 768.0f);
                    float ml = (*pML + *(pML + 1) + *(pML + 2)) * (1.0f / 768.0f);
                    float mr = (*pMR + *(pMR + 1) + *(pMR + 2)) * (1.0f / 768.0f);
                    float bl = (*pBL + *(pBL + 1) + *(pBL + 2)) * (1.0f / 768.0f);
                    float bm = (*pBM + *(pBM + 1) + *(pBM + 2)) * (1.0f / 768.0f);
                    float br = (*pBR + *(pBR + 1) + *(pBR + 2)) * (1.0f / 768.0f);

                    float sobelX = tr + 2.0f * mr + br - tl - 2.0f * ml - bl;
                    float sobelY = bl + 2.0f * bm + br - tl - 2.0f * tm - tr;

                    sobelX *= bumpStrength;
                    sobelY *= bumpStrength;

                    float nx = sobelX;
                    float ny = sobelY;
                    float nz = 1.0f;

                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 0.0001f)
                    {
                        nx /= len;
                        ny /= len;
                        nz /= len;
                    }

                    int rnx = (int)(nx * 0.5f * 255.0f + 127.5f);
                    int rny = (int)(ny * 0.5f * 255.0f + 127.5f);
                    int rnz = (int)(nz * 0.5f * 255.0f + 127.5f);

                    if (rnx > 255) rnx = 255;
                    if (rnx < 0) rnx = 0;
                    if (rny > 255) rny = 255;
                    if (rny < 0) rny = 0;
                    if (rnz > 255) rnz = 255;
                    if (rnz < 0) rnz = 0;

                    *p = (byte)rnz;
                    *(p + 1) = (byte)rny;
                    *(p + 2) = (byte)rnx;
                    *(p + 3) = 255;
                }
            }

            ProcessBorders(data, width, height, stride);
        }

        private static unsafe void ProcessBorders(byte* data, int width, int height, int stride)
        {
            for (int x = 0; x < width; x++)
            {
                uint sample = 0x7F7FFF00;

                byte* pTop = data + x * 4;
                *(uint*)pTop = sample;

                byte* pBottom = data + (height - 1) * stride + x * 4;
                *(uint*)pBottom = sample;
            }

            for (int y = 0; y < height; y++)
            {
                uint sample = 0x7F7FFF00;

                byte* pLeft = data + y * stride;
                *(uint*)pLeft = sample;

                byte* pRight = data + y * stride + (width - 1) * 4;
                *(uint*)pRight = sample;
            }
        }
    }
}
