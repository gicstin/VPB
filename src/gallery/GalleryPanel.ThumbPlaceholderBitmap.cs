using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>Bakes gallery thumb placeholder strings to shared textures (no per-cell live Text).</summary>
    internal static class ThumbPlaceholderLabelBitmapCache
    {
        private const int MaxCacheEntries = 384;
        private const int MaxBakesPerFrame = 4;
        private const int MinBakeSize = 32;
        private const int MaxBakeSize = 256;

        private struct CacheEntry
        {
            public Texture2D Texture;
            public int LastUsedFrame;
        }

        private struct BakeRequest
        {
            public long Key;
            public string Display;
            public int FontSize;
            public int PixelSize;
        }

        private static readonly Dictionary<long, CacheEntry> s_Cache = new Dictionary<long, CacheEntry>();
        private static readonly Queue<BakeRequest> s_Pending = new Queue<BakeRequest>();
        private static GameObject s_Root;
        private static Canvas s_Canvas;
        private static Camera s_Cam;
        private static Text s_Text;
        private static RectTransform s_TextRt;
        private static RenderTexture s_Rt;
        private static int s_RtSize;
        private static int s_BakeLayer = -1;
        private static int s_BakesThisFrame;
        private static int s_BakeFrame = -1;

        public static int QuantizeBakePixelSize(float cellSidePx)
        {
            int px = Mathf.RoundToInt(cellSidePx);
            px = (px + 7) & ~7;
            return Mathf.Clamp(px, MinBakeSize, MaxBakeSize);
        }

        public static long MakeKey(string display, int fontSize, int pixelSize)
        {
            unchecked
            {
                long h = 2166136261L;
                if (!string.IsNullOrEmpty(display))
                {
                    for (int i = 0; i < display.Length; i++)
                        h = (h ^ display[i]) * 16777619L;
                }
                return h ^ ((long)fontSize << 24) ^ (uint)pixelSize;
            }
        }

        public static bool TryGetTexture(string display, int fontSize, int pixelSize, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrEmpty(display) || fontSize < 1 || pixelSize < MinBakeSize)
                return false;

            long key = MakeKey(display, fontSize, pixelSize);
            CacheEntry entry;
            if (s_Cache.TryGetValue(key, out entry) && entry.Texture != null)
            {
                entry.LastUsedFrame = Time.frameCount;
                s_Cache[key] = entry;
                texture = entry.Texture;
                return true;
            }

            if (Time.frameCount != s_BakeFrame)
            {
                s_BakeFrame = Time.frameCount;
                s_BakesThisFrame = 0;
            }

            if (s_BakesThisFrame >= MaxBakesPerFrame)
            {
                EnqueuePending(key, display, fontSize, pixelSize);
                return false;
            }

            Texture2D baked = BakeNow(display, fontSize, pixelSize);
            if (baked == null)
                return false;

            s_BakesThisFrame++;
            Store(key, baked);
            texture = baked;
            return true;
        }

        public static int ProcessPendingQueue(int maxBakes)
        {
            if (maxBakes <= 0 || s_Pending.Count == 0) return 0;
            int done = 0;
            while (done < maxBakes && s_Pending.Count > 0)
            {
                BakeRequest req = s_Pending.Dequeue();
                if (s_Cache.ContainsKey(req.Key))
                {
                    CacheEntry hit;
                    if (s_Cache.TryGetValue(req.Key, out hit) && hit.Texture != null)
                        continue;
                }

                Texture2D baked = BakeNow(req.Display, req.FontSize, req.PixelSize);
                if (baked == null) continue;
                Store(req.Key, baked);
                done++;
            }
            return done;
        }

        private static void EnqueuePending(long key, string display, int fontSize, int pixelSize)
        {
            if (s_Pending.Count > 256) return;
            s_Pending.Enqueue(new BakeRequest
            {
                Key = key,
                Display = display,
                FontSize = fontSize,
                PixelSize = pixelSize
            });
        }

        private static void Store(long key, Texture2D baked)
        {
            EvictIfNeeded();
            s_Cache[key] = new CacheEntry { Texture = baked, LastUsedFrame = Time.frameCount };
        }

        private static void EvictIfNeeded()
        {
            if (s_Cache.Count < MaxCacheEntries) return;
            int oldestFrame = int.MaxValue;
            long oldestKey = 0;
            foreach (var kv in s_Cache)
            {
                if (kv.Value.LastUsedFrame >= oldestFrame) continue;
                oldestFrame = kv.Value.LastUsedFrame;
                oldestKey = kv.Key;
            }
            CacheEntry victim;
            if (s_Cache.TryGetValue(oldestKey, out victim))
            {
                try
                {
                    if (victim.Texture != null)
                        UnityEngine.Object.Destroy(victim.Texture);
                }
                catch { }
                s_Cache.Remove(oldestKey);
            }
        }

        private static Texture2D BakeNow(string display, int fontSize, int pixelSize)
        {
            try
            {
                EnsureBakeRig(pixelSize);
                s_Text.fontSize = fontSize;
                s_Text.text = display ?? "";
                Canvas.ForceUpdateCanvases();

                RenderTexture prev = RenderTexture.active;
                bool camWasEnabled = s_Cam.enabled;
                try
                {
                    RenderTexture.active = s_Rt;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = null;

                    s_Cam.targetTexture = s_Rt;
                    s_Cam.enabled = true;
                    s_Cam.Render();
                    s_Cam.targetTexture = null;
                    s_Cam.enabled = camWasEnabled;

                    var tex = new Texture2D(pixelSize, pixelSize, TextureFormat.ARGB32, false);
                    tex.name = "VPB_ThumbPlaceholderLabel";
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    RenderTexture.active = s_Rt;
                    tex.ReadPixels(new Rect(0f, 0f, pixelSize, pixelSize), 0, 0, false);
                    tex.Apply(false, false);
                    if (!TextureHasVisibleLabel(tex))
                    {
                        UnityEngine.Object.Destroy(tex);
                        return null;
                    }
                    return tex;
                }
                finally
                {
                    RenderTexture.active = prev;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool TextureHasVisibleLabel(Texture2D tex)
        {
            if (tex == null) return false;
            try
            {
                Color32[] px = tex.GetPixels32();
                if (px == null || px.Length == 0) return false;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a > 8) return true;
                }
            }
            catch { }
            return false;
        }

        private static void EnsureBakeRig(int pixelSize)
        {
            if (s_Root != null && s_Rt != null && s_RtSize == pixelSize)
                return;

            DestroyBakeRig();
            s_RtSize = pixelSize;

            int layer = GetBakeLayer();
            const float bakeWorldX = 12000f;
            const float bakeWorldY = 12000f;

            s_Root = new GameObject("VPB_ThumbPlaceholderLabelBake");
            s_Root.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(s_Root);
            SetLayerRecursive(s_Root, layer);
            s_Root.transform.position = new Vector3(bakeWorldX, bakeWorldY, 0f);
            s_Root.transform.localScale = Vector3.one;

            RectTransform canvasRt = s_Root.AddComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(pixelSize, pixelSize);

            s_Canvas = s_Root.AddComponent<Canvas>();
            s_Canvas.renderMode = RenderMode.WorldSpace;
            s_Canvas.pixelPerfect = false;

            GameObject camGo = new GameObject("Cam");
            camGo.transform.SetParent(s_Root.transform, false);
            SetLayerRecursive(camGo, layer);
            s_Cam = camGo.AddComponent<Camera>();
            s_Cam.clearFlags = CameraClearFlags.SolidColor;
            s_Cam.backgroundColor = Color.clear;
            s_Cam.orthographic = true;
            s_Cam.orthographicSize = pixelSize * 0.5f;
            s_Cam.aspect = 1f;
            s_Cam.nearClipPlane = 0.3f;
            s_Cam.farClipPlane = 2000f;
            s_Cam.cullingMask = 1 << layer;
            s_Cam.enabled = false;
            camGo.transform.localPosition = new Vector3(0f, 0f, -100f);
            camGo.transform.localRotation = Quaternion.identity;

            s_Canvas.worldCamera = s_Cam;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(s_Root.transform, false);
            SetLayerRecursive(textGo, layer);
            s_TextRt = textGo.AddComponent<RectTransform>();
            s_TextRt.anchorMin = Vector2.zero;
            s_TextRt.anchorMax = Vector2.one;
            s_TextRt.offsetMin = new Vector2(3f, 3f);
            s_TextRt.offsetMax = new Vector2(-3f, -3f);

            s_Text = textGo.AddComponent<Text>();
            s_Text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            s_Text.lineSpacing = 1f;
            s_Text.color = new Color(0.88f, 0.88f, 0.92f, 1f);
            s_Text.alignment = TextAnchor.MiddleCenter;
            s_Text.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_Text.verticalOverflow = VerticalWrapMode.Truncate;
            s_Text.resizeTextForBestFit = false;
            s_Text.supportRichText = false;
            s_Text.raycastTarget = false;

            s_Rt = RenderTexture.GetTemporary(pixelSize, pixelSize, 0, RenderTextureFormat.ARGB32);
        }

        private static int GetBakeLayer()
        {
            if (s_BakeLayer >= 0) return s_BakeLayer;
            s_BakeLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (s_BakeLayer < 0) s_BakeLayer = 2;
            return s_BakeLayer;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        private static void DestroyBakeRig()
        {
            try
            {
                if (s_Rt != null)
                {
                    RenderTexture.ReleaseTemporary(s_Rt);
                    s_Rt = null;
                }
            }
            catch { }

            try
            {
                if (s_Root != null)
                    UnityEngine.Object.Destroy(s_Root);
            }
            catch { }

            s_Root = null;
            s_Canvas = null;
            s_Cam = null;
            s_Text = null;
            s_TextRt = null;
            s_RtSize = 0;
        }
    }
}
