using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Optional libjpeg-turbo (<c>turbojpeg.dll</c> under BepInEx/plugins) via TurboJPEG C API.
    /// Windows MSVC builds use 32-bit <c>unsigned long</c> for <c>jpegSize</c> — delegate uses <c>uint</c>.
    /// </summary>
    internal static class TurboJpegNative
    {
        private const int TJPF_RGB = 0;
        /// <summary>Output rows bottom-up to match Unity <see cref="Texture2D.LoadRawTextureData"/> (OpenGL-style: first row = image bottom).</summary>
        private const int TJFLAG_BOTTOMUP = 2;
        private const int TJFLAG_FASTUPSAMPLE = 256;
        private const int TJFLAG_FASTDCT = 2048;

        private static bool s_LoadAttempted;
        private static bool s_LoadOk;
        private static string s_LoadDetail;

        private static D_tjInitDecompress s_tjInitDecompress;
        private static D_tjDestroy s_tjDestroy;
        private static D_tjDecompressHeader3 s_tjDecompressHeader3;
        private static D_tjDecompress2 s_tjDecompress2;
        private static D_tjGetErrorStr s_tjGetErrorStr;
        private static D_tjGetErrorStr2 s_tjGetErrorStr2;

        private static volatile bool s_SessionGiveUp;
        private static int s_ConsecutiveFailures;

        /// <summary>One decompress handle per worker thread (TurboJPEG API is not thread-safe across concurrent use).</summary>
        [ThreadStatic]
        private static IntPtr s_ThreadTjHandle;

        /// <summary>Grow-only native RGB buffer for this thread's last decode size.</summary>
        [ThreadStatic]
        private static IntPtr s_ThreadNativeRgb;

        [ThreadStatic]
        private static int s_ThreadNativeRgbBytes;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr D_tjInitDecompress();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_tjDestroy(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_tjDecompressHeader3(IntPtr handle, IntPtr jpegBuf, uint jpegSize, out int width, out int height, out int jpegSubsamp, out int colorspace);

        /// <summary>
        /// Order per turbojpeg.h: width, <c>pitch</c> (bytes per row), height — pitch required (use width * 3 for packed RGB).
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int D_tjDecompress2(IntPtr handle, IntPtr jpegBuf, uint jpegSize, IntPtr dstBuf, int width, int pitch, int height, int pixelFormat, int flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr D_tjGetErrorStr();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr D_tjGetErrorStr2(IntPtr handle);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        internal static bool IsAvailable
        {
            get
            {
                EnsureLoaded();
                return s_LoadOk;
            }
        }

        internal static string LastLoadDetail
        {
            get
            {
                EnsureLoaded();
                return s_LoadDetail ?? "";
            }
        }

        internal static bool SessionGiveUpDecode => s_SessionGiveUp;

        internal static void ResetSessionGiveUp()
        {
            s_SessionGiveUp = false;
            s_ConsecutiveFailures = 0;
        }

        internal static bool ShouldAttemptTurboDecode()
        {
            EnsureLoaded();
            return s_LoadOk && !s_SessionGiveUp;
        }

        internal static bool LooksLikeJpeg(byte[] buf, int len)
        {
            return buf != null && len >= 3 && buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF;
        }

        /// <summary>Maps gallery grid column count (1–12) to TurboJPEG integer scale denominator 1, 2, or 4 (more columns → smaller decode; 8+ cols share denom 4).</summary>
        internal static int ScaleDenomFromGridColumns(int gridColumnCount)
        {
            int c = gridColumnCount;
            if (c < 1) c = 1;
            if (c > 12) c = 12;
            if (c <= 4) return 1;
            if (c <= 7) return 2;
            return 4;
        }

        /// <summary>Clamp to supported integer-scale denominators for <c>tjDecompress2</c>.</summary>
        internal static int NormalizeScaleDenom(int denom)
        {
            if (denom <= 1) return 1;
            if (denom <= 2) return 2;
            if (denom <= 4) return 4;
            return 8;
        }

        /// <summary>Pick largest supported denominator such that scaled dimensions stay ≥1 pixel.</summary>
        internal static int EffectiveScaleDenom(int requestedDenom, int jpegWidth, int jpegHeight)
        {
            int d = NormalizeScaleDenom(requestedDenom);
            while (d > 1 && (jpegWidth < d || jpegHeight < d))
                d >>= 1;
            return d;
        }

        /// <summary>Exact-length RGB buffers for <see cref="Texture2D.LoadRawTextureData"/>; bounded stacks per size.</summary>
        private static class RgbBytePool
        {
            private static readonly object Gate = new object();
            private static readonly Dictionary<int, Stack<byte[]>> Pools = new Dictionary<int, Stack<byte[]>>();
            private const int MaxPerLength = 8;

            internal static byte[] Rent(int length)
            {
                if (length <= 0) return new byte[0];
                lock (Gate)
                {
                    Stack<byte[]> st;
                    if (Pools.TryGetValue(length, out st) && st != null && st.Count > 0)
                        return st.Pop();
                }
                return new byte[length];
            }

            internal static void Return(byte[] arr)
            {
                if (arr == null || arr.Length == 0) return;
                lock (Gate)
                {
                    Stack<byte[]> st;
                    if (!Pools.TryGetValue(arr.Length, out st) || st == null)
                    {
                        st = new Stack<byte[]>();
                        Pools[arr.Length] = st;
                    }
                    if (st.Count < MaxPerLength)
                        st.Push(arr);
                }
            }
        }

        private static IntPtr GetThreadDecompressHandle(ref string error)
        {
            EnsureLoaded();
            error = null;
            if (!s_LoadOk || s_tjInitDecompress == null)
            {
                error = string.IsNullOrEmpty(s_LoadDetail) ? "turbojpeg not loaded" : s_LoadDetail;
                return IntPtr.Zero;
            }
            IntPtr h = s_ThreadTjHandle;
            if (h == IntPtr.Zero)
            {
                h = s_tjInitDecompress();
                if (h == IntPtr.Zero)
                {
                    error = "tjInitDecompress failed";
                    return IntPtr.Zero;
                }
                s_ThreadTjHandle = h;
            }
            return h;
        }

        private static IntPtr RentThreadNativeRgb(int rgbLen)
        {
            if (s_ThreadNativeRgb == IntPtr.Zero || s_ThreadNativeRgbBytes < rgbLen)
            {
                if (s_ThreadNativeRgb != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(s_ThreadNativeRgb); } catch { }
                    s_ThreadNativeRgb = IntPtr.Zero;
                    s_ThreadNativeRgbBytes = 0;
                }
                s_ThreadNativeRgb = Marshal.AllocHGlobal(rgbLen);
                s_ThreadNativeRgbBytes = rgbLen;
            }
            return s_ThreadNativeRgb;
        }

        internal static bool TryDecodeJpegToTexture2D(byte[] jpegBytes, int jpegLength, out Texture2D tex, out string error)
        {
            return TryDecodeJpegToTexture2D(jpegBytes, jpegLength, 1, out tex, out error);
        }

        /// <param name="scaleDenom">1 = full resolution; 2, 4, 8 = decode at 1/N per TurboJPEG integer scaling.</param>
        internal static bool TryDecodeJpegToTexture2D(byte[] jpegBytes, int jpegLength, int scaleDenom, out Texture2D tex, out string error)
        {
            tex = null;
            error = null;
            if (jpegBytes == null || jpegLength <= 0 || jpegLength > jpegBytes.Length)
            {
                error = "invalid buffer";
                return false;
            }
            EnsureLoaded();
            if (!s_LoadOk || s_tjDecompressHeader3 == null || s_tjDecompress2 == null)
            {
                error = string.IsNullOrEmpty(s_LoadDetail) ? "turbojpeg not loaded" : s_LoadDetail;
                return false;
            }

            string initErr = null;
            IntPtr handle = GetThreadDecompressHandle(ref initErr);
            if (handle == IntPtr.Zero)
            {
                error = initErr ?? "tjInitDecompress failed";
                return false;
            }

            GCHandle pin = GCHandle.Alloc(jpegBytes, GCHandleType.Pinned);
            try
            {
                IntPtr jpegPtr = pin.AddrOfPinnedObject();
                int w, h, subsamp, cs;
                int hr = s_tjDecompressHeader3(handle, jpegPtr, (uint)jpegLength, out w, out h, out subsamp, out cs);
                if (hr != 0 || w <= 0 || h <= 0)
                {
                    error = "tjDecompressHeader3 rc=" + hr + " " + w + "x" + h + " " + FormatTjError(handle);
                    NoteFailure(error);
                    return false;
                }

                int effDenom = EffectiveScaleDenom(scaleDenom, w, h);
                // libjpeg-turbo TJSCALED(dim, 1/N) = ceil(dim / N). Floor division under-allocates
                // by one pixel when dim isn't a multiple of N: tjDecompress2 silently widens output
                // to its natural scaled width and writes past the row stride we passed, producing
                // diagonal drift visible as a noise stripe on the right (BOTTOMUP → also on top).
                int outW = effDenom <= 1 ? w : Math.Max(1, (w + effDenom - 1) / effDenom);
                int outH = effDenom <= 1 ? h : Math.Max(1, (h + effDenom - 1) / effDenom);
                int flags = TJFLAG_BOTTOMUP | TJFLAG_FASTUPSAMPLE | TJFLAG_FASTDCT;

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int rgbLen = outW * outH * 3;
                    IntPtr dst = RentThreadNativeRgb(rgbLen);
                    int pitchRgb = outW * 3;
                    int dr = s_tjDecompress2(handle, jpegPtr, (uint)jpegLength, dst, outW, pitchRgb, outH, TJPF_RGB, flags);
                    if (dr != 0)
                    {
                        if (attempt == 0 && effDenom > 1)
                        {
                            effDenom = 1;
                            outW = w;
                            outH = h;
                            continue;
                        }
                        error = "tjDecompress2 rc=" + dr + " " + FormatTjError(handle);
                        NoteFailure(error);
                        return false;
                    }

                    try { Thread.Sleep(0); } catch { }

                    byte[] rgb = RgbBytePool.Rent(rgbLen);
                    try
                    {
                        Marshal.Copy(dst, rgb, 0, rgbLen);
                        try
                        {
                            tex = new Texture2D(outW, outH, TextureFormat.RGB24, false, false);
                            tex.LoadRawTextureData(rgb);
                            tex.Apply(false, false);
                            tex.name = effDenom > 1 ? ("TurboJpeg/" + effDenom) : "TurboJpeg";
                        }
                        catch (Exception ex)
                        {
                            error = "Texture2D: " + ex.Message;
                            NoteFailure(error);
                            if (tex != null)
                            {
                                try { UnityEngine.Object.Destroy(tex); } catch { }
                                tex = null;
                            }
                            return false;
                        }

                        s_ConsecutiveFailures = 0;
                        return true;
                    }
                    finally
                    {
                        RgbBytePool.Return(rgb);
                    }
                }

                error = "tjDecompress2 exhausted retries";
                return false;
            }
            finally
            {
                pin.Free();
            }
        }

        private const int GiveUpAfterFailures = 12;

        private static void NoteFailure(string err)
        {
            int n = System.Threading.Interlocked.Increment(ref s_ConsecutiveFailures);
            if (n == 1)
            {
                try
                {
                    LogUtil.LogWarning("[VPB] TurboJPEG decode failed (first): " + (err ?? "") + " — Check turbojpeg.dll (libjpeg-turbo); giving up after " + GiveUpAfterFailures + " failures.");
                }
                catch { }
            }
            else if (n == GiveUpAfterFailures && !s_SessionGiveUp)
            {
                s_SessionGiveUp = true;
                try
                {
                    LogUtil.LogWarning("[VPB] TurboJPEG: session give-up (" + GiveUpAfterFailures + " failures). Unity-only JPEG decode until restart. Last: " + (err ?? ""));
                }
                catch { }
            }
        }

        private static string FormatTjError(IntPtr handle)
        {
            try
            {
                if (s_tjGetErrorStr2 != null && handle != IntPtr.Zero)
                {
                    IntPtr p = s_tjGetErrorStr2(handle);
                    if (p != IntPtr.Zero)
                    {
                        string s = Marshal.PtrToStringAnsi(p);
                        if (!string.IsNullOrEmpty(s)) return "tjerr=" + s;
                    }
                }
                if (s_tjGetErrorStr != null)
                {
                    IntPtr p = s_tjGetErrorStr();
                    if (p != IntPtr.Zero)
                    {
                        string s = Marshal.PtrToStringAnsi(p);
                        if (!string.IsNullOrEmpty(s)) return "tjerr=" + s;
                    }
                }
            }
            catch { }
            return "";
        }

        private static void EnsureLoaded()
        {
            if (s_LoadAttempted) return;
            s_LoadAttempted = true;
            try
            {
                string[] candidates = BuildTurboJpegCandidates();
                for (int i = 0; i < candidates.Length; i++)
                {
                    string path = candidates[i];
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                    IntPtr mod = LoadLibrary(path);
                    if (mod == IntPtr.Zero) continue;

                    s_tjInitDecompress = LoadDelegate<D_tjInitDecompress>(mod, "tjInitDecompress");
                    s_tjDestroy = LoadDelegate<D_tjDestroy>(mod, "tjDestroy");
                    s_tjDecompressHeader3 = LoadDelegate<D_tjDecompressHeader3>(mod, "tjDecompressHeader3");
                    s_tjDecompress2 = LoadDelegate<D_tjDecompress2>(mod, "tjDecompress2");
                    s_tjGetErrorStr = LoadDelegate<D_tjGetErrorStr>(mod, "tjGetErrorStr");
                    s_tjGetErrorStr2 = LoadDelegate<D_tjGetErrorStr2>(mod, "tjGetErrorStr2");

                    if (s_tjInitDecompress != null && s_tjDestroy != null && s_tjDecompressHeader3 != null && s_tjDecompress2 != null)
                    {
                        s_LoadOk = true;
                        s_LoadDetail = path;
                        return;
                    }

                    ClearDelegates();
                }
                s_LoadDetail = "no turbojpeg.dll with required exports (tjInitDecompress, tjDestroy, tjDecompressHeader3, tjDecompress2)";
            }
            catch (Exception ex)
            {
                s_LoadDetail = ex.Message;
            }
        }

        private static T LoadDelegate<T>(IntPtr module, string name) where T : class
        {
            IntPtr p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        private static void ClearDelegates()
        {
            s_tjInitDecompress = null;
            s_tjDestroy = null;
            s_tjDecompressHeader3 = null;
            s_tjDecompress2 = null;
            s_tjGetErrorStr = null;
            s_tjGetErrorStr2 = null;
        }

        private static string SafeAssemblyDirectory()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(loc)) return "";
                return Path.GetDirectoryName(loc) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void AddCandidate(List<string> list, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase)) return;
            }
            list.Add(path);
        }

        private static string[] BuildTurboJpegCandidates()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string pluginsRel = Path.GetFullPath(Path.Combine(Path.Combine("BepInEx", "plugins"), "turbojpeg.dll"));
            string asmDir = SafeAssemblyDirectory();

            var list = new List<string>(16);
            AddCandidate(list, pluginsRel);
            AddCandidate(list, Path.Combine(Path.Combine(Path.Combine(baseDir, "BepInEx"), "plugins"), "turbojpeg.dll"));
            if (!string.IsNullOrEmpty(asmDir) && asmDir.IndexOf(Path.Combine("BepInEx", "plugins"), StringComparison.OrdinalIgnoreCase) >= 0)
                AddCandidate(list, Path.Combine(asmDir, "turbojpeg.dll"));
            AddCandidate(list, Path.Combine(asmDir, "turbojpeg.dll"));
            return list.ToArray();
        }
    }
}
