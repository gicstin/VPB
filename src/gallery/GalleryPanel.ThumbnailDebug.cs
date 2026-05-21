using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Shift+Right-click on a gallery row dumps a forensic snapshot of the row's thumbnail
        // pipeline to Cache/VPB/_thumbdebug/<timestamp>_<row>/. Used to localize sporadic
        // artifacts (rectangular bands of pixel noise) to one of: disk cache bytes,
        // TurboJPEG scaled decode, Unity LoadImage decode, or display-side stride.
        internal void DebugDumpThumbnailForRow(FileEntry file, GameObject rowRoot)
        {
            LogUtil.Log("[VPB ThumbDbg] DebugDumpThumbnailForRow entry: file=" + (file != null ? file.Name : "<null>") + " rowRoot=" + (rowRoot != null ? rowRoot.name : "<null>") + " isActive=" + (isActiveAndEnabled));
            if (file == null) { LogUtil.LogWarning("[VPB ThumbDbg] aborted: file is null"); return; }
            if (!isActiveAndEnabled) { LogUtil.LogWarning("[VPB ThumbDbg] aborted: panel not active, can't StartCoroutine"); return; }
            try { StartCoroutine(DebugDumpThumbnailCo(file, rowRoot)); }
            catch (Exception ex) { LogUtil.LogError("[VPB ThumbDbg] dispatch failed: " + ex); }
        }

        internal static bool IsTextureLogVerbose()
        {
            try
            {
                return Settings.Instance != null
                    && Settings.Instance.TextureLogLevel != null
                    && Settings.Instance.TextureLogLevel.Value >= 2;
            }
            catch { return false; }
        }

        // Toolbar handler: dumps the first selected file's thumbnail pipeline. The row's RawImage is
        // located by walking active UIFileEntryLeftReleaseSelect components — recycled grid items still
        // carry the bound FileEntry. If the row isn't currently visible, dump proceeds without displayed.png.
        private void TboxDumpThumbnailDebugForSelection()
        {
            FileEntry target = null;
            try
            {
                if (selectedFiles != null && selectedFiles.Count > 0) target = selectedFiles[0];
            }
            catch { target = null; }
            if (target == null)
            {
                try { ShowTemporaryStatus("Thumb debug: select a file first", 2.5f); } catch { }
                return;
            }

            GameObject rowRoot = FindActiveRowGameObjectForFile(target);
            if (rowRoot == null)
                LogUtil.LogWarning("[VPB ThumbDbg] tbox click: row for selected file is not currently rendered; proceeding without displayed.png. file=" + target.Name);
            DebugDumpThumbnailForRow(target, rowRoot);
        }

        private static GameObject FindActiveRowGameObjectForFile(FileEntry target)
        {
            if (target == null) return null;
            UIFileEntryLeftReleaseSelect[] all = UnityEngine.Object.FindObjectsOfType<UIFileEntryLeftReleaseSelect>();
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                UIFileEntryLeftReleaseSelect r = all[i];
                if (r == null || r.File == null) continue;
                if (ReferenceEquals(r.File, target)) return r.gameObject;
                if (!string.IsNullOrEmpty(r.File.Path) && !string.IsNullOrEmpty(target.Path)
                    && string.Equals(r.File.Path, target.Path, StringComparison.OrdinalIgnoreCase))
                    return r.gameObject;
            }
            return null;
        }

        private IEnumerator DebugDumpThumbnailCo(FileEntry file, GameObject rowRoot)
        {
            string outDir;
            try { outDir = BuildThumbDebugDir(file); }
            catch (Exception ex) { LogUtil.LogError("[VPB ThumbDbg] mkdir failed: " + ex); yield break; }

            StringBuilder meta = new StringBuilder(4096);
            AppendMeta(meta, "vpb_version", PluginVersionInfo.Version);
            AppendMeta(meta, "timestamp_utc", DateTime.UtcNow.ToString("o"));
            AppendMeta(meta, "file.Path", file.Path);
            AppendMeta(meta, "file.Uid", file.Uid);
            AppendMeta(meta, "file.Name", file.Name);
            AppendMeta(meta, "file.Type", file.GetType().FullName);

            RawImage thumbRaw = FindThumbnailRawImageInRow(rowRoot);
            string boundImgPath = null;
            if (thumbRaw != null)
            {
                ThumbnailBindingTag bind = thumbRaw.GetComponent<ThumbnailBindingTag>();
                if (bind != null && !string.IsNullOrEmpty(bind.ExpectedTag))
                {
                    int sep = bind.ExpectedTag.IndexOf('|');
                    boundImgPath = sep >= 0 ? bind.ExpectedTag.Substring(sep + 1) : bind.ExpectedTag;
                    AppendMeta(meta, "bind.ExpectedTag", bind.ExpectedTag);
                    AppendMeta(meta, "bind.ThumbRetryCount", bind.ThumbRetryCount);
                }
                AppendMeta(meta, "rawimage.uvRect", thumbRaw.uvRect);
                AspectRatioFitter arf = thumbRaw.GetComponent<AspectRatioFitter>();
                AppendMeta(meta, "rawimage.aspectRatioFitter", arf != null ? arf.aspectRatio.ToString("0.0000") : "<none>");

                Texture2D disp = thumbRaw.texture as Texture2D;
                if (disp != null)
                {
                    AppendMeta(meta, "displayed.format", disp.format);
                    AppendMeta(meta, "displayed.width", disp.width);
                    AppendMeta(meta, "displayed.height", disp.height);
                    AppendMeta(meta, "displayed.name", disp.name);
                    try
                    {
                        byte[] png = CaptureTextureToPng(thumbRaw.texture, disp.width, disp.height);
                        if (png != null && png.Length > 0)
                            File.WriteAllBytes(Path.Combine(outDir, "displayed.png"), png);
                    }
                    catch (Exception ex) { AppendMeta(meta, "displayed.png.error", ex.Message); }
                }
                else
                {
                    AppendMeta(meta, "displayed.texture", "<null>");
                }
            }
            else
            {
                AppendMeta(meta, "rawimage", "<not found in row>");
            }

            int gridCols = EffectiveGridColumnsForThumbDecode();
            int gridDenom = TurboJpegNative.ScaleDenomFromGridColumns(gridCols);
            AppendMeta(meta, "resolved.imgPath", boundImgPath);
            AppendMeta(meta, "grid.columns", gridCols);
            AppendMeta(meta, "grid.denom", gridDenom);
            AppendMeta(meta, "turbojpeg.available", TurboJpegNative.IsAvailable);
            AppendMeta(meta, "turbojpeg.lastLoadDetail", TurboJpegNative.LastLoadDetail);
            AppendMeta(meta, "turbojpeg.sessionGaveUp", TurboJpegNative.SessionGiveUpDecode);
            int texLogLvl = 0;
            try { if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null) texLogLvl = Settings.Instance.TextureLogLevel.Value; } catch { }
            AppendMeta(meta, "settings.TextureLogLevel", texLogLvl);

            if (!string.IsNullOrEmpty(boundImgPath))
            {
                DumpDiskCacheForAllDenoms(boundImgPath, outDir, meta);
                yield return null;
                yield return StartCoroutine(DumpFreshDecode(boundImgPath, outDir, meta, gridDenom));
            }

            try { File.WriteAllText(Path.Combine(outDir, "meta.txt"), meta.ToString()); }
            catch (Exception ex) { LogUtil.LogError("[VPB ThumbDbg] meta.txt write failed: " + ex); }

            LogUtil.Log("[VPB ThumbDbg] dumped to " + outDir);
            try { ShowTemporaryStatus("Thumb debug: " + Path.GetFileName(outDir), 4f); } catch { }
        }

        private static void DumpDiskCacheForAllDenoms(string imgPath, string outDir, StringBuilder meta)
        {
            long lastWriteTime = 0;
            bool isPkgPath = GalleryThumbnailCache.Instance.IsPackagePath(imgPath);
            if (isPkgPath) lastWriteTime = 0;
            else
            {
                try
                {
                    FileEntry fe = FileManager.GetFileEntry(imgPath);
                    if (fe != null) lastWriteTime = fe.LastWriteTime.ToFileTime();
                }
                catch { }
            }
            AppendMeta(meta, "cache.lastWriteTimeFt", lastWriteTime);
            AppendMeta(meta, "cache.isPackagePath", isPkgPath);

            int[] denomsToProbe = new int[] { 1, 2, 4, 8 };
            for (int i = 0; i < denomsToProbe.Length; i++)
            {
                int d = denomsToProbe[i];
                string ck = GalleryThumbnailCache.Instance.GetThumbnailCacheKey(imgPath, d);
                byte[] data; int w, h; TextureFormat fmt;
                bool hit = GalleryThumbnailCache.Instance.TryGetThumbnail(imgPath, lastWriteTime, out data, out w, out h, out fmt, d);
                AppendMeta(meta, "cache[d=" + d + "].key", ck);
                AppendMeta(meta, "cache[d=" + d + "].hit", hit);
                if (!hit || data == null) continue;

                int expected = TextureUtil.GetExpectedRawDataSize(w, h, fmt);
                AppendMeta(meta, "cache[d=" + d + "].w", w);
                AppendMeta(meta, "cache[d=" + d + "].h", h);
                AppendMeta(meta, "cache[d=" + d + "].fmt", fmt);
                AppendMeta(meta, "cache[d=" + d + "].expectedBytes", expected);
                AppendMeta(meta, "cache[d=" + d + "].rentedBufferLength", data.Length);

                string binName = "cached_d" + d + ".bin";
                string pngName = "cached_d" + d + ".png";
                try { File.WriteAllBytes(Path.Combine(outDir, binName), TrimBuffer(data, expected)); }
                catch (Exception ex) { AppendMeta(meta, "cache[d=" + d + "].bin.error", ex.Message); }

                try
                {
                    Texture2D t = new Texture2D(w, h, fmt, false);
                    TextureUtil.SafeLoadRawTextureData(t, data, w, h, fmt);
                    t.Apply(false, false);
                    byte[] png = t.EncodeToPNG();
                    if (png != null && png.Length > 0)
                        File.WriteAllBytes(Path.Combine(outDir, pngName), png);
                    UnityEngine.Object.Destroy(t);
                }
                catch (Exception ex) { AppendMeta(meta, "cache[d=" + d + "].png.error", ex.Message); }

                try { ByteArrayPool.Return(data); } catch { }
            }
        }

        private IEnumerator DumpFreshDecode(string imgPath, string outDir, StringBuilder meta, int gridDenom)
        {
            byte[] src = null;
            string readErr = null;
            try
            {
                if (FileManager.FileExists(imgPath))
                {
                    using (FileEntryStream fes = FileManager.OpenStream(imgPath))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[81920];
                        int n;
                        while ((n = fes.Stream.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                        src = ms.ToArray();
                    }
                }
            }
            catch (Exception ex) { readErr = ex.Message; }

            if (readErr != null) AppendMeta(meta, "source.read.error", readErr);
            if (src == null || src.Length == 0)
            {
                AppendMeta(meta, "source.bytes", "<missing>");
                yield break;
            }

            AppendMeta(meta, "source.bytes.length", src.Length);
            AppendMeta(meta, "source.looksLikeJpeg", TurboJpegNative.LooksLikeJpeg(src, src.Length));
            try { File.WriteAllBytes(Path.Combine(outDir, "source.bin"), src); }
            catch (Exception ex) { AppendMeta(meta, "source.bin.error", ex.Message); }

            yield return null;

            // Unity decode (full resolution)
            try
            {
                Texture2D u = new Texture2D(2, 2);
                bool ok = u.LoadImage(src);
                AppendMeta(meta, "source.decode.unity.loadImage", ok);
                if (ok)
                {
                    AppendMeta(meta, "source.decode.unity.size", u.width + "x" + u.height);
                    AppendMeta(meta, "source.decode.unity.format", u.format);
                    byte[] png = u.EncodeToPNG();
                    if (png != null && png.Length > 0)
                        File.WriteAllBytes(Path.Combine(outDir, "source_decode_unity.png"), png);
                }
                UnityEngine.Object.Destroy(u);
            }
            catch (Exception ex) { AppendMeta(meta, "source.decode.unity.error", ex.Message); }

            yield return null;

            // TurboJPEG at the denom the grid would have used, plus full-res for comparison.
            DumpTurboDecode(src, gridDenom, outDir, meta, "source_decode_turbo_grid.png");
            if (gridDenom != 1)
                DumpTurboDecode(src, 1, outDir, meta, "source_decode_turbo_full.png");
        }

        private static void DumpTurboDecode(byte[] src, int denom, string outDir, StringBuilder meta, string outName)
        {
            string key = "source.decode.turbo[" + denom + "]";
            try
            {
                if (!TurboJpegNative.IsAvailable)
                {
                    AppendMeta(meta, key, "<unavailable>");
                    return;
                }
                Texture2D tex; string err;
                if (TurboJpegNative.TryDecodeJpegToTexture2D(src, src.Length, denom, out tex, out err) && tex != null)
                {
                    AppendMeta(meta, key + ".size", tex.width + "x" + tex.height);
                    AppendMeta(meta, key + ".format", tex.format);
                    byte[] png = tex.EncodeToPNG();
                    if (png != null && png.Length > 0)
                        File.WriteAllBytes(Path.Combine(outDir, outName), png);
                    UnityEngine.Object.Destroy(tex);
                }
                else
                {
                    AppendMeta(meta, key + ".error", err ?? "<unknown>");
                }
            }
            catch (Exception ex) { AppendMeta(meta, key + ".exception", ex.Message); }
        }

        private static void AppendMeta(StringBuilder sb, string key, object value)
        {
            sb.Append(key).Append(": ").Append(value == null ? "<null>" : value.ToString()).Append('\n');
        }

        private static string BuildThumbDebugDir(FileEntry file)
        {
            string root = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Cache"), "VPB");
            string dbg = Path.Combine(root, "_thumbdebug");
            if (!Directory.Exists(dbg)) Directory.CreateDirectory(dbg);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string safe = TextureUtil.SanitizeFileName(file.Name ?? "row");
            if (safe.Length > 60) safe = safe.Substring(0, 60);
            string outDir = Path.Combine(dbg, stamp + "_" + safe);
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        private static RawImage FindThumbnailRawImageInRow(GameObject rowRoot)
        {
            if (rowRoot == null) return null;
            RawImage[] all = rowRoot.GetComponentsInChildren<RawImage>(true);
            if (all == null || all.Length == 0) return null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetComponent<ThumbnailBindingTag>() != null)
                    return all[i];
            }
            return all[0];
        }

        private static byte[] TrimBuffer(byte[] src, int len)
        {
            if (src == null) return new byte[0];
            if (len <= 0 || len >= src.Length) return src;
            byte[] dst = new byte[len];
            Buffer.BlockCopy(src, 0, dst, 0, len);
            return dst;
        }

        // Works for both readable and non-readable textures (Blit through ARGB32 RT, ReadPixels into a fresh Texture2D).
        private static byte[] CaptureTextureToPng(Texture src, int w, int h)
        {
            if (src == null || w <= 0 || h <= 0) return new byte[0];
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                copy.Apply(false, false);
                byte[] png = copy.EncodeToPNG();
                UnityEngine.Object.Destroy(copy);
                return png;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
