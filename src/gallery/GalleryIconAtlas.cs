using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using UnityEngine;

namespace VPB
{
    internal static class GalleryIconAtlas
    {
        public const string PackFileName = "vpb_icons.pack";
        public const string OverrideDirName = "vpb_icons_override";

        private static int _overrideDirState;

        private const uint FlagDeflated = 1u;
        private const uint FlagAlphaOnly = 2u;
        private static readonly byte[] Magic = { 0x56, 0x50, 0x42, 0x49, 0x43, 0x4F, 0x4E, 0x31 };

        private static Texture2D _texture;
        private static Dictionary<string, Rect> _rects;
        private static bool _loadAttempted;
        private static string _loadError;

        public static bool IsLoaded { get { return _texture != null && _rects != null; } }
        public static Texture2D Texture { get { return _texture; } }
        public static int IconCount { get { return _rects != null ? _rects.Count : 0; } }
        public static string LoadError { get { return _loadError; } }

        /// <summary>Atlas key = Tabler source id (<c>shirt-off</c>, <c>filled/star</c>). Also accepts leftover <c>vpb_icons/*.png</c> paths.</summary>
        public static string ToAtlasKey(string roleOrPath)
        {
            if (string.IsNullOrEmpty(roleOrPath)) return null;
            string s = roleOrPath.Replace('\\', '/').Trim();
            const string leftoverPrefix = "vpb_icons/";
            if (s.StartsWith(leftoverPrefix, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(leftoverPrefix.Length);
            if (s.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            return s.Length == 0 ? null : s.ToLowerInvariant();
        }

        public static string OverridePathFor(string roleOrPath)
        {
            if (_overrideDirState == 0)
            {
                try
                {
                    _overrideDirState = Directory.Exists(Path.Combine(BepInEx.Paths.PluginPath, OverrideDirName)) ? 1 : 2;
                }
                catch { _overrideDirState = 2; }
            }
            if (_overrideDirState != 1) return null;
            string key = ToAtlasKey(roleOrPath);
            return key == null ? null : OverrideDirName + "/" + key + ".png";
        }

        public static bool TryGetRect(string relativePathFromPluginsDir, out Rect rect)
        {
            rect = default(Rect);
            if (!IsLoaded) return false;
            string key = ToAtlasKey(relativePathFromPluginsDir);
            if (key == null) return false;
            return _rects.TryGetValue(key, out rect);
        }

        public static bool EnsureLoaded()
        {
            if (IsLoaded) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;

            try
            {
                string packPath = Path.Combine(BepInEx.Paths.PluginPath, PackFileName);
                if (!File.Exists(packPath))
                {
                    _loadError = "pack not found: " + packPath;
                    return ReportFailure();
                }
                if (LoadFrom(packPath)) return true;
                return ReportFailure();
            }
            catch (Exception e)
            {
                _loadError = e.Message;
                DestroyTexture();
                _loadAttempted = true;
                return ReportFailure();
            }
        }

        private static bool ReportFailure()
        {
            Debug.LogWarning("[VPB] Icon atlas unavailable (" + (_loadError ?? "unknown") +
                             "); falling back to loose PNGs in " + OverrideDirName + "/.");
            return false;
        }

        private static bool LoadFrom(string packPath)
        {
            int width, height;
            byte[] alpha;
            Dictionary<string, Rect> rects;

            using (FileStream fs = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024))
            {
                byte[] head = ReadExact(fs, 8 + 24);
                for (int i = 0; i < Magic.Length; i++)
                {
                    if (head[i] != Magic[i]) { _loadError = "bad magic"; return false; }
                }

                uint version = ReadU32(head, 8);
                uint flags = ReadU32(head, 12);
                width = (int)ReadU32(head, 16);
                height = (int)ReadU32(head, 20);
                uint count = ReadU32(head, 28);

                if (version != 1u) { _loadError = "unsupported version " + version; return false; }
                if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
                {
                    _loadError = "bad atlas size " + width + "x" + height;
                    return false;
                }
                if ((flags & FlagAlphaOnly) == 0u) { _loadError = "unsupported payload layout"; return false; }

                rects = new Dictionary<string, Rect>((int)count * 2, StringComparer.OrdinalIgnoreCase);
                byte[] nameBuf = new byte[256];
                byte[] entryTail = new byte[8];

                for (uint i = 0; i < count; i++)
                {
                    int nameLen = ReadU16(ReadExact(fs, 2), 0);
                    if (nameLen <= 0 || nameLen > nameBuf.Length) { _loadError = "bad name length"; return false; }
                    ReadExact(fs, nameLen, nameBuf);
                    string name = Encoding.UTF8.GetString(nameBuf, 0, nameLen).ToLowerInvariant();
                    if (name.EndsWith(".png", StringComparison.Ordinal))
                        name = name.Substring(0, name.Length - 4);

                    ReadExact(fs, 8, entryTail);
                    int x = ReadU16(entryTail, 0);
                    int y = ReadU16(entryTail, 2);
                    int w = ReadU16(entryTail, 4);
                    int h = ReadU16(entryTail, 6);
                    if (x < 0 || y < 0 || x + w > width || y + h > height) { _loadError = "rect out of bounds: " + name; return false; }
                    rects[name] = new Rect(x, y, w, h);
                }

                byte[] sizes = ReadExact(fs, 8);
                int rawLen = (int)ReadU32(sizes, 0);
                int compLen = (int)ReadU32(sizes, 4);
                if (rawLen != width * height) { _loadError = "payload size mismatch"; return false; }

                byte[] compressed = ReadExact(fs, compLen);
                alpha = new byte[rawLen];

                if ((flags & FlagDeflated) != 0u)
                {
                    Inflater inflater = new Inflater(true);
                    inflater.SetInput(compressed, 0, compLen);
                    int got = 0;
                    while (got < rawLen && !inflater.IsFinished)
                    {
                        int n = inflater.Inflate(alpha, got, rawLen - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got != rawLen) { _loadError = "inflate produced " + got + " of " + rawLen; return false; }
                }
                else
                {
                    Buffer.BlockCopy(compressed, 0, alpha, 0, rawLen);
                }
            }

            byte[] rgba = new byte[alpha.Length * 4];
            for (int i = 0, o = 0; i < alpha.Length; i++)
            {
                rgba[o++] = 255;
                rgba[o++] = 255;
                rgba[o++] = 255;
                rgba[o++] = alpha[i];
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = "VPB_IconAtlas";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 0;
            tex.LoadRawTextureData(rgba);
            tex.Apply(false, true);

            DestroyTexture();
            _texture = tex;
            _rects = rects;
            _loadError = null;
            return true;
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            byte[] buf = new byte[count];
            ReadExact(s, count, buf);
            return buf;
        }

        private static void ReadExact(Stream s, int count, byte[] buf)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) throw new EndOfStreamException("vpb_icons.pack truncated");
                off += n;
            }
        }

        private static uint ReadU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        private static int ReadU16(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8);
        }

        private static void DestroyTexture()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
        }

        public static void Destroy()
        {
            DestroyTexture();
            _rects = null;
            _loadAttempted = false;
            _overrideDirState = 0;
        }
    }
}
