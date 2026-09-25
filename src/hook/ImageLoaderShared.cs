using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VPB
{
    internal static class ImageLoaderShared
    {
        internal static bool IsPowerOfTwo(uint x)
        {
            return x != 0 && (x & (x - 1)) == 0;
        }

        internal static string DiskCachePath(string imgPath, string diskCacheSignature)
        {
            FileEntry fileEntry = FileManager.GetFileEntry(imgPath);
            string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (fileEntry == null || textureCacheDir == null) return null;
            string fileName = Path.GetFileName(imgPath).Replace('.', '_');
            return textureCacheDir + "/" + fileName + "_" + fileEntry.Size + "_" + fileEntry.LastWriteTime.ToFileTime() + "_" + diskCacheSignature + ".vamcache";
        }

        internal static string WebCachePath(string imgPath, string diskCacheSignature)
        {
            string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (textureCacheDir == null) return null;
            string text = imgPath.Replace("https://", string.Empty)
                .Replace("http://", string.Empty)
                .Replace("/", "__")
                .Replace("?", "_");
            return textureCacheDir + "/" + text + "_" + diskCacheSignature + ".vamcache";
        }

        internal static void ApplyTransformations(byte[] raw, int num8, int width, int height,
            bool isNormalMap, bool invert, bool createAlphaFromGrayscale, bool createNormalFromBump,
            float bumpStrength, bool compress, string imgPath)
        {
            if (isNormalMap)
            {
                for (int i = 0; i < num8; i += 4)
                {
                    raw[i + 3] = byte.MaxValue;
                }
            }

            if (invert)
            {
                for (int j = 0; j < num8; j++)
                {
                    raw[j] = (byte)(255 - raw[j]);
                }
            }

            if (createAlphaFromGrayscale)
            {
                bool hasExistingAlpha = false;
                for (int k = 3; k < num8; k += 4)
                {
                    if (raw[k] != byte.MaxValue)
                    {
                        hasExistingAlpha = true;
                        break;
                    }
                }

                if (!hasExistingAlpha)
                {
                    for (int k = 0; k < num8; k += 4)
                    {
                        int avg = (raw[k] + raw[k + 1] + raw[k + 2]) / 3;
                        raw[k + 3] = (byte)avg;
                    }
                }

                bool enforceDxt5 = compress && imgPath != null && imgPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                if (enforceDxt5)
                {
                    raw[3] = 128;
                }
            }

            if (createNormalFromBump)
            {
                byte[] array = new byte[num8];
                float[][] hMap = new float[height][];
                for (int l = 0; l < height; l++)
                {
                    hMap[l] = new float[width];
                    for (int m = 0; m < width; m++)
                    {
                        int idx = (l * width + m) * 4;
                        hMap[l][m] = (raw[idx] + raw[idx + 1] + raw[idx + 2]) / 768f;
                    }
                }

                Vector3 v = default(Vector3);
                for (int n = 0; n < height; n++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float h21 = 0.5f, h22 = 0.5f, h23 = 0.5f, h24 = 0.5f, h25 = 0.5f, h26 = 0.5f, h27 = 0.5f, h28 = 0.5f;
                        int xm1 = x - 1, xp1 = x + 1, yp1 = n + 1, ym1 = n - 1;

                        if (yp1 < height && xm1 >= 0) h21 = hMap[yp1][xm1];
                        if (xm1 >= 0) h22 = hMap[n][xm1];
                        if (ym1 >= 0 && xm1 >= 0) h23 = hMap[ym1][xm1];
                        if (yp1 < height) h24 = hMap[yp1][x];
                        if (ym1 >= 0) h25 = hMap[ym1][x];
                        if (yp1 < height && xp1 < width) h26 = hMap[yp1][xp1];
                        if (xp1 < width) h27 = hMap[n][xp1];
                        if (ym1 >= 0 && xp1 < width) h28 = hMap[ym1][xp1];

                        float nx = h26 + 2f * h27 + h28 - h21 - 2f * h22 - h23;
                        float ny = h23 + 2f * h25 + h28 - h21 - 2f * h24 - h26;
                        v.x = nx * bumpStrength;
                        v.y = ny * bumpStrength;
                        v.z = 1f;
                        v.Normalize();

                        int idx = (n * width + x) * 4;
                        raw[idx] = (byte)((v.x * 0.5f + 0.5f) * 255f);
                        raw[idx + 1] = (byte)((v.y * 0.5f + 0.5f) * 255f);
                        raw[idx + 2] = (byte)((v.z * 0.5f + 0.5f) * 255f);
                        raw[idx + 3] = byte.MaxValue;
                    }
                }
            }

        }
    }

    public class PriorityQueue<T>
    {
        public List<T> data;
        private Comparison<T> comparison;

        public PriorityQueue(Comparison<T> comparison)
        {
            this.data = new List<T>();
            this.comparison = comparison;
        }

        public void Remove(T item)
        {
            data.Remove(item);
            // List.Remove shifts indices and breaks heap order, so rebuild.
            int count = data.Count;
            for (int i = count / 2; i >= 0; i--)
            {
                HeapifyDown(i);
            }
        }

        private void HeapifyDown(int pi)
        {
            int li = data.Count - 1;
            while (true)
            {
                int ci = pi * 2 + 1;
                if (ci > li) break;
                int rc = ci + 1;
                if (rc <= li && comparison(data[rc], data[ci]) < 0) ci = rc;
                if (comparison(data[pi], data[ci]) <= 0) break;
                T tmp = data[pi]; data[pi] = data[ci]; data[ci] = tmp;
                pi = ci;
            }
        }

        public void Enqueue(T item)
        {
            data.Add(item);
            int ci = data.Count - 1;
            while (ci > 0)
            {
                int pi = (ci - 1) / 2;
                if (comparison(data[ci], data[pi]) >= 0) break;
                T tmp = data[ci]; data[ci] = data[pi]; data[pi] = tmp;
                ci = pi;
            }
        }

        public T Dequeue()
        {
            int li = data.Count - 1;
            T frontItem = data[0];
            data[0] = data[li];
            data.RemoveAt(li);

            --li;
            if (li >= 0) HeapifyDown(0);

            return frontItem;
        }

        public T Peek()
        {
            if (data.Count == 0) return default(T);
            return data[0];
        }

        public int Count
        {
            get { return data.Count; }
        }

        public void Clear()
        {
            data.Clear();
        }
    }
}
