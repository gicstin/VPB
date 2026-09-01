using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public static class VpbNetClipFormat
    {
        public const int MagicSize = 8;
        public const ushort Version = 1;
        public const int FixedHeaderBytes = 32;
        public const int FloatsPerController = 7;
        public const long MaxClipBytes = 64L * 1024L * 1024L;

        public static readonly byte[] Magic = { (byte)'V', (byte)'P', (byte)'B', (byte)'C', (byte)'L', (byte)'I', (byte)'P', 0 };

        public const string ClipFolder = "clips";
        public const string ClipExtension = ".vpbclip";

        public static string ClipDirectory
        {
            get { return VpbPaths.Clips; }
        }

        public static int FrameBytes(int controllerCount)
        {
            return 4 + (1 + FloatsPerController * controllerCount) * 4;
        }

        public static bool MagicMatches(byte[] buf)
        {
            for (int i = 0; i < MagicSize; i++)
            {
                if (buf[i] != Magic[i]) return false;
            }
            return true;
        }

        public static string ResolvePath(string name)
        {
            string dir = ClipDirectory;
            if (string.IsNullOrEmpty(name)) return null;

            string trimmed = name.Trim();
            if (trimmed.Length == 0) return null;
            if (trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 || trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0
                || trimmed.IndexOf(':') >= 0)
                return trimmed;

            if (!trimmed.EndsWith(ClipExtension, StringComparison.OrdinalIgnoreCase)) trimmed += ClipExtension;
            return Path.Combine(dir, trimmed);
        }

        public static string NewestClipPath()
        {
            try
            {
                string dir = ClipDirectory;
                if (!Directory.Exists(dir)) return null;

                string[] files = Directory.GetFiles(dir, "*" + ClipExtension);
                string best = null;
                DateTime bestTime = DateTime.MinValue;
                for (int i = 0; i < files.Length; i++)
                {
                    DateTime t = File.GetLastWriteTimeUtc(files[i]);
                    if (best != null && t <= bestTime) continue;
                    best = files[i];
                    bestTime = t;
                }
                return best;
            }
            catch { return null; }
        }

        public static string TimestampedName()
        {
            DateTime n = DateTime.Now;
            return string.Format("clip-{0:0000}{1:00}{2:00}-{3:00}{4:00}{5:00}{6}",
                n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, ClipExtension);
        }
    }

    public sealed class VpbNetPoseRecorder
    {
        const int BlockBytes = 256 * 1024;
        const int BlockCount = 8;

        readonly Queue<byte[]> _free = new Queue<byte[]>();
        readonly Queue<int> _pendingUsed = new Queue<int>();
        readonly Queue<byte[]> _pending = new Queue<byte[]>();
        readonly object _lock = new object();

        AutoResetEvent _signal;
        Thread _thread;
        FileStream _stream;
        float[] _scratch;
        byte[] _block;
        int _blockUsed;
        int _count;
        int _frameBytes;
        volatile bool _running;

        public string Path { get; private set; }
        public uint Frames { get; private set; }
        public int Dropped { get; private set; }
        public bool IsOpen { get { return _stream != null; } }
        public string LastError { get; private set; }

        public bool Open(string path, string[] names, int count, float nominalHz)
        {
            Close();
            LastError = null;

            if (count <= 0 || names == null || names.Length < count)
            {
                LastError = "no controllers to record";
                return false;
            }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
            }
            catch (Exception e)
            {
                LastError = "cannot write " + path + ": " + e.Message;
                _stream = null;
                return false;
            }

            Path = path;
            _count = count;
            _frameBytes = VpbNetClipFormat.FrameBytes(count);
            _scratch = new float[1 + VpbNetClipFormat.FloatsPerController * count];
            Frames = 0;
            Dropped = 0;

            if (!WriteHeader(names, count, nominalHz))
            {
                Close();
                return false;
            }

            for (int i = 0; i < BlockCount; i++) _free.Enqueue(new byte[BlockBytes]);
            _block = _free.Dequeue();
            _blockUsed = 0;

            _signal = new AutoResetEvent(false);
            _running = true;
            _thread = new Thread(WriteLoop);
            _thread.IsBackground = true;
            _thread.Name = "VpbNetClipWriter";
            _thread.Start();
            return true;
        }

        bool WriteHeader(string[] names, int count, float nominalHz)
        {
            try
            {
                StringBuilder sb = new StringBuilder(256);
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(names[i]);
                }
                byte[] nameBytes = Encoding.UTF8.GetBytes(sb.ToString());

                byte[] head = new byte[VpbNetClipFormat.FixedHeaderBytes + nameBytes.Length];
                Buffer.BlockCopy(VpbNetClipFormat.Magic, 0, head, 0, VpbNetClipFormat.MagicSize);
                VpbIpc.WriteU16(head, 8, VpbNetClipFormat.Version);
                VpbIpc.WriteU16(head, 10, 0);
                VpbIpc.WriteU16(head, 12, count);
                VpbIpc.WriteU16(head, 14, nameBytes.Length);
                VpbIpc.WriteU32(head, 16, 0);
                float[] hz = { nominalHz };
                Buffer.BlockCopy(hz, 0, head, 20, 4);
                VpbIpc.WriteI64(head, 24, DateTime.UtcNow.Ticks);
                Buffer.BlockCopy(nameBytes, 0, head, VpbNetClipFormat.FixedHeaderBytes, nameBytes.Length);

                _stream.Write(head, 0, head.Length);
                return true;
            }
            catch (Exception e)
            {
                LastError = "header write failed: " + e.Message;
                return false;
            }
        }

        public bool WriteFrame(uint seq, float time, Vector3[] pos, Quaternion[] rot, int count)
        {
            if (!_running || _block == null || count != _count) return false;

            if (_blockUsed + _frameBytes > BlockBytes && !RotateBlock())
            {
                Dropped++;
                return false;
            }

            VpbIpc.WriteU32(_block, _blockUsed, seq);

            float[] s = _scratch;
            s[0] = time;
            int f = 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = pos[i];
                Quaternion r = rot[i];
                s[f] = p.x;
                s[f + 1] = p.y;
                s[f + 2] = p.z;
                s[f + 3] = r.x;
                s[f + 4] = r.y;
                s[f + 5] = r.z;
                s[f + 6] = r.w;
                f += VpbNetClipFormat.FloatsPerController;
            }

            Buffer.BlockCopy(s, 0, _block, _blockUsed + 4, f * 4);
            _blockUsed += _frameBytes;
            Frames++;
            return true;
        }

        bool RotateBlock()
        {
            if (!TryRotate())
            {
                try { Thread.Sleep(0); }
                catch { }
                if (!TryRotate()) return false;
            }

            _blockUsed = 0;
            try { _signal.Set(); }
            catch { }
            return true;
        }

        bool TryRotate()
        {
            lock (_lock)
            {
                if (_free.Count == 0) return false;
                _pending.Enqueue(_block);
                _pendingUsed.Enqueue(_blockUsed);
                _block = _free.Dequeue();
            }
            try { _signal.Set(); }
            catch { }
            return true;
        }

        void WriteLoop()
        {
            while (true)
            {
                try
                {
                    if (_signal != null) _signal.WaitOne(250);
                    if (!DrainOnce() && !_running) return;
                }
                catch { if (!_running) return; }
            }
        }

        bool DrainOnce()
        {
            bool any = false;
            while (true)
            {
                byte[] block;
                int used;
                lock (_lock)
                {
                    if (_pending.Count == 0) return any;
                    block = _pending.Dequeue();
                    used = _pendingUsed.Dequeue();
                }

                try
                {
                    FileStream s = _stream;
                    if (s != null && used > 0) s.Write(block, 0, used);
                }
                catch (Exception e) { LastError = "write failed: " + e.Message; }

                lock (_lock) { _free.Enqueue(block); }
                any = true;
            }
        }

        public void Close()
        {
            if (_stream == null)
            {
                _running = false;
                return;
            }

            if (_blockUsed > 0)
            {
                lock (_lock)
                {
                    _pending.Enqueue(_block);
                    _pendingUsed.Enqueue(_blockUsed);
                }
                _block = null;
                _blockUsed = 0;
            }

            _running = false;
            try { if (_signal != null) _signal.Set(); }
            catch { }
            try { if (_thread != null && _thread.IsAlive && !_thread.Join(3000)) _thread.Abort(); }
            catch { }
            _thread = null;

            try { DrainOnce(); }
            catch { }

            try
            {
                _stream.Flush();
                _stream.Seek(16, SeekOrigin.Begin);
                byte[] four = new byte[4];
                VpbIpc.WriteU32(four, 0, Frames);
                _stream.Write(four, 0, 4);
                _stream.Flush();
            }
            catch (Exception e) { LastError = "close failed: " + e.Message; }

            try { _stream.Close(); }
            catch { }
            _stream = null;

            try { if (_signal != null) _signal.Close(); }
            catch { }
            _signal = null;

            lock (_lock)
            {
                _pending.Clear();
                _pendingUsed.Clear();
                _free.Clear();
            }
            _block = null;
        }
    }

    public sealed class VpbNetPoseClip
    {
        string[] _names;
        float[] _times;
        float[] _data;
        int _count;
        int _frames;
        int _searchHint;

        public string Path { get; private set; }
        public float NominalHz { get; private set; }
        public DateTime RecordedUtc { get; private set; }
        public bool Truncated { get; private set; }
        public string LastError { get; private set; }

        public int ControllerCount { get { return _count; } }
        public int FrameCount { get { return _frames; } }
        public string[] Names { get { return _names; } }
        public float Duration { get { return _frames > 0 ? _times[_frames - 1] : 0f; } }

        public static VpbNetPoseClip Load(string path, out string error)
        {
            error = null;
            VpbNetPoseClip clip = new VpbNetPoseClip();
            if (!clip.LoadInternal(path))
            {
                error = clip.LastError;
                return null;
            }
            return clip;
        }

        bool LoadInternal(string path)
        {
            byte[] all;
            try
            {
                if (!File.Exists(path))
                {
                    LastError = "no clip at " + path;
                    return false;
                }

                FileInfo fi = new FileInfo(path);
                if (fi.Length > VpbNetClipFormat.MaxClipBytes)
                {
                    LastError = "clip is " + (fi.Length / (1024 * 1024)) + " MB, over the "
                        + (VpbNetClipFormat.MaxClipBytes / (1024 * 1024)) + " MB limit";
                    return false;
                }
                if (fi.Length < VpbNetClipFormat.FixedHeaderBytes)
                {
                    LastError = "clip is too small to hold a header";
                    return false;
                }

                all = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                LastError = "cannot read " + path + ": " + e.Message;
                return false;
            }

            if (!VpbNetClipFormat.MagicMatches(all))
            {
                LastError = "not a VPB clip file";
                return false;
            }

            int version = VpbIpc.ReadU16(all, 8);
            if (version != VpbNetClipFormat.Version)
            {
                LastError = "clip is format v" + version + ", this build reads v" + VpbNetClipFormat.Version;
                return false;
            }

            _count = VpbIpc.ReadU16(all, 12);
            int nameBytes = VpbIpc.ReadU16(all, 14);
            uint declaredFrames = VpbIpc.ReadU32(all, 16);
            float[] hz = new float[1];
            Buffer.BlockCopy(all, 20, hz, 0, 4);
            NominalHz = hz[0];
            try { RecordedUtc = new DateTime(VpbIpc.ReadI64(all, 24), DateTimeKind.Utc); }
            catch { RecordedUtc = DateTime.MinValue; }

            if (_count <= 0 || _count > 256)
            {
                LastError = "clip declares " + _count + " controllers";
                return false;
            }

            int dataStart = VpbNetClipFormat.FixedHeaderBytes + nameBytes;
            if (dataStart > all.Length)
            {
                LastError = "clip header is truncated";
                return false;
            }

            try { _names = Encoding.UTF8.GetString(all, VpbNetClipFormat.FixedHeaderBytes, nameBytes).Split('\n'); }
            catch { _names = new string[0]; }
            if (_names.Length < _count)
            {
                LastError = "clip names table lists " + _names.Length + " of " + _count + " controllers";
                return false;
            }

            int frameBytes = VpbNetClipFormat.FrameBytes(_count);
            int available = (all.Length - dataStart) / frameBytes;
            _frames = declaredFrames > 0 && declaredFrames <= (uint)available ? (int)declaredFrames : available;
            Truncated = declaredFrames == 0 || (int)declaredFrames != available;

            if (_frames <= 0)
            {
                LastError = "clip holds no frames";
                return false;
            }

            int perFrame = VpbNetClipFormat.FloatsPerController * _count;
            _times = new float[_frames];
            _data = new float[_frames * perFrame];
            float[] scratch = new float[1 + perFrame];

            for (int f = 0; f < _frames; f++)
            {
                int off = dataStart + f * frameBytes + 4;
                Buffer.BlockCopy(all, off, scratch, 0, (1 + perFrame) * 4);
                _times[f] = scratch[0];
                Buffer.BlockCopy(scratch, 4, _data, f * perFrame * 4, perFrame * 4);
            }

            Path = path;
            return true;
        }

        public int MapTo(string[] targetNames, int[] mapOut)
        {
            int matched = 0;
            for (int i = 0; i < _count; i++)
            {
                mapOut[i] = -1;
                for (int j = 0; j < targetNames.Length; j++)
                {
                    if (!string.Equals(_names[i], targetNames[j], StringComparison.Ordinal)) continue;
                    mapOut[i] = j;
                    matched++;
                    break;
                }
            }
            return matched;
        }

        public void Sample(float t, Vector3[] posOut, Quaternion[] rotOut)
        {
            if (_frames == 1 || t <= _times[0])
            {
                Read(0, posOut, rotOut);
                return;
            }
            if (t >= _times[_frames - 1])
            {
                Read(_frames - 1, posOut, rotOut);
                return;
            }

            int lo = _searchHint;
            if (lo < 0 || lo >= _frames - 1 || _times[lo] > t || _times[lo + 1] < t) lo = FindSpan(t);
            _searchHint = lo;

            float span = _times[lo + 1] - _times[lo];
            float u = span > 0.00001f ? (t - _times[lo]) / span : 0f;
            ReadLerp(lo, lo + 1, u, posOut, rotOut);
        }

        int FindSpan(float t)
        {
            int lo = 0;
            int hi = _frames - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_times[mid] <= t) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        void Read(int frame, Vector3[] posOut, Quaternion[] rotOut)
        {
            int b = frame * VpbNetClipFormat.FloatsPerController * _count;
            for (int i = 0; i < _count; i++)
            {
                int o = b + i * VpbNetClipFormat.FloatsPerController;
                posOut[i] = new Vector3(_data[o], _data[o + 1], _data[o + 2]);
                rotOut[i] = new Quaternion(_data[o + 3], _data[o + 4], _data[o + 5], _data[o + 6]);
            }
        }

        void ReadLerp(int a, int b, float u, Vector3[] posOut, Quaternion[] rotOut)
        {
            int pf = VpbNetClipFormat.FloatsPerController * _count;
            int ab = a * pf;
            int bb = b * pf;
            for (int i = 0; i < _count; i++)
            {
                int oa = ab + i * VpbNetClipFormat.FloatsPerController;
                int ob = bb + i * VpbNetClipFormat.FloatsPerController;
                posOut[i] = Vector3.Lerp(
                    new Vector3(_data[oa], _data[oa + 1], _data[oa + 2]),
                    new Vector3(_data[ob], _data[ob + 1], _data[ob + 2]), u);
                rotOut[i] = Quaternion.Slerp(
                    new Quaternion(_data[oa + 3], _data[oa + 4], _data[oa + 5], _data[oa + 6]),
                    new Quaternion(_data[ob + 3], _data[ob + 4], _data[ob + 5], _data[ob + 6]), u);
            }
        }

        public void Rewind()
        {
            _searchHint = 0;
        }
    }
}
