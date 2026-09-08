using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VpbNet.Speech
{
    public sealed class ProcessAudioCapture : IDisposable
    {
        const int QueueSize = 12;
        readonly object _gate = new object();
        readonly byte[][] _frames = new byte[QueueSize][];
        readonly long[] _times = new long[QueueSize];
        readonly byte[] _pending = new byte[VpbNetSpeech.FrameBytes];
        readonly ManualResetEvent _stop;
        readonly int _pid;
        int _read, _count, _pendingCount;
        volatile bool _disposed;
        volatile bool _finished;
        volatile bool _started;
        string _error;

        public ProcessAudioCapture(int pid)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                throw new PlatformNotSupportedException("Process audio capture requires Windows build 20348 or newer.");
            if (pid <= 0) throw new ArgumentOutOfRangeException("pid");
            _pid = pid;
            for (int i = 0; i < QueueSize; i++) _frames[i] = new byte[VpbNetSpeech.FrameBytes];
            _stop = new ManualResetEvent(false);
            Thread worker = new Thread(Run) { IsBackground = true, Name = "VPB speech capture" };
            try
            {
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();
            }
            catch { _stop.Dispose(); throw; }
        }

        public bool Finished { get { return _finished; } }
        public bool Started { get { return _started; } }
        public string Error { get { return Volatile.Read(ref _error); } }

        public bool TryRead(byte[] target, int offset)
        {
            lock (_gate)
            {
                if (_disposed) return false;
                long now = Stopwatch.GetTimestamp();
                while (_count > 0)
                {
                    int slot = _read;
                    _read = (_read + 1) % QueueSize;
                    _count--;
                    if (now - _times[slot] > Stopwatch.Frequency / 4) continue;
                    Buffer.BlockCopy(_frames[slot], 0, target, offset, VpbNetSpeech.FrameBytes);
                    Array.Clear(_frames[slot], 0, VpbNetSpeech.FrameBytes);
                    return true;
                }
                return false;
            }
        }

        void Append(byte[] bytes, int count)
        {
            int offset = 0;
            while (offset < count && !_disposed)
            {
                int copy = Math.Min(count - offset, _pending.Length - _pendingCount);
                Buffer.BlockCopy(bytes, offset, _pending, _pendingCount, copy);
                offset += copy;
                _pendingCount += copy;
                if (_pendingCount != _pending.Length) continue;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (_count == QueueSize) { _read = (_read + 1) % QueueSize; _count--; }
                    int slot = (_read + _count) % QueueSize;
                    Buffer.BlockCopy(_pending, 0, _frames[slot], 0, _pending.Length);
                    _times[slot] = Stopwatch.GetTimestamp();
                    _count++;
                }
                _pendingCount = 0;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        void Run()
        {
            IAudioClient client = null;
            IAudioCaptureClient capture = null;
            object service = null;
            byte[] buffer = null;
            Activation activation = null;
            AutoResetEvent sampleReady = null;
            try
            {
                using (Process process = Process.GetProcessById(_pid))
                {
                    sampleReady = new AutoResetEvent(false);
                    if (!string.Equals(process.ProcessName, "TTSVoiceWizard", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Selected process is not TTSVoiceWizard.");
                    activation = new Activation(_pid);
                    for (int i = 0; !activation.Completion.IsCompleted && i < 100 && !_disposed; i++) _stop.WaitOne(100);
                    if (_disposed) return;
                    if (!activation.Completion.IsCompleted) throw new TimeoutException("Windows audio activation timed out.");
                    client = activation.Take();
                    WaveFormat format = new WaveFormat
                    {
                        Tag = 1, Channels = 1, Rate = VpbNetSpeech.SampleRate, Bits = 16,
                        Block = 2, BytesPerSecond = VpbNetSpeech.SampleRate * 2
                    };
                    Marshal.ThrowExceptionForHR(client.Initialize(0, 0x88060000, 0, 0, ref format, IntPtr.Zero));
                    Guid captureId = typeof(IAudioCaptureClient).GUID;
                    Marshal.ThrowExceptionForHR(client.GetService(ref captureId, out service));
                    capture = (IAudioCaptureClient)service;
                    Marshal.ThrowExceptionForHR(client.SetEventHandle(sampleReady.SafeWaitHandle.DangerousGetHandle()));
                    buffer = new byte[VpbNetSpeech.FrameBytes * 5];
                    WaitHandle[] waits = { _stop, sampleReady };
                    Marshal.ThrowExceptionForHR(client.Start());
                    _started = true;
                    while (!_disposed && !process.HasExited)
                    {
                        if (WaitHandle.WaitAny(waits, 100) == 0) break;
                        uint frames;
                        Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out frames));
                        while (frames != 0 && !_disposed)
                        {
                            IntPtr data;
                            uint flags;
                            ulong position, timestamp;
                            Marshal.ThrowExceptionForHR(capture.GetBuffer(out data, out frames, out flags, out position, out timestamp));
                            try
                            {
                                if (frames > VpbNetSpeech.SampleRate) throw new InvalidOperationException("Unexpected audio packet size.");
                                if ((flags & 1) != 0) _pendingCount = 0;
                                int bytes = checked((int)frames * 2);
                                for (int at = 0; at < bytes; at += buffer.Length)
                                {
                                    int count = Math.Min(buffer.Length, bytes - at);
                                    if ((flags & 2) != 0) Array.Clear(buffer, 0, count);
                                    else Marshal.Copy(IntPtr.Add(data, at), buffer, 0, count);
                                    Append(buffer, count);
                                }
                            }
                            finally { Marshal.ThrowExceptionForHR(capture.ReleaseBuffer(frames)); }
                            Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out frames));
                        }
                    }
                }
            }
            catch (Exception e) { Volatile.Write(ref _error, "Process audio capture failed (" + e.GetType().Name + ", 0x" + e.HResult.ToString("X8") + ")."); }
            finally
            {
                if (buffer != null) Array.Clear(buffer, 0, buffer.Length);
                if (client != null) { try { client.Stop(); } catch (COMException) { } }
                if (service != null && Marshal.IsComObject(service)) Marshal.ReleaseComObject(service);
                if (client != null && Marshal.IsComObject(client)) Marshal.ReleaseComObject(client);
                if (activation != null) activation.Dispose();
                if (sampleReady != null) sampleReady.Dispose();
                lock (_gate)
                {
                    for (int i = 0; i < QueueSize; i++) Array.Clear(_frames[i], 0, _frames[i].Length);
                    Array.Clear(_pending, 0, _pending.Length);
                    _count = 0;
                    _started = false;
                    _stop.Dispose();
                    _finished = true;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (!_finished) _stop.Set();
                for (int i = 0; i < QueueSize; i++) Array.Clear(_frames[i], 0, _frames[i].Length);
                _count = 0;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class Activation : ICompletionHandler, IAgileObject, IDisposable
        {
            readonly object _gate = new object();
            readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IntPtr _parameters;
            IntPtr _variant;
            object _client;
            int _result;
            bool _disposed;

            public Activation(int pid)
            {
                try
                {
                    _parameters = Marshal.AllocHGlobal(12);
                    Marshal.WriteInt32(_parameters, 0, 1);
                    Marshal.WriteInt32(_parameters, 4, pid);
                    Marshal.WriteInt32(_parameters, 8, 0);
                    _variant = Marshal.AllocHGlobal(24);
                    for (int i = 0; i < 24; i++) Marshal.WriteByte(_variant, i, 0);
                    Marshal.WriteInt16(_variant, 0, 65);
                    Marshal.WriteInt32(_variant, 8, 12);
                    Marshal.WriteIntPtr(_variant, 16, _parameters);
                    Guid id = typeof(IAudioClient).GUID;
                    IActivationOperation operation;
                    int result = ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref id, _variant, this, out operation);
                    if (operation != null) Marshal.ReleaseComObject(operation);
                    Marshal.ThrowExceptionForHR(result);
                }
                catch { lock (_gate) FreeParameters(); throw; }
            }

            public Task Completion { get { return _completion.Task; } }

            public int ActivateCompleted(IActivationOperation operation)
            {
                lock (_gate)
                {
                    try
                    {
                        int call = operation.GetActivateResult(out _result, out _client);
                        if (call < 0) _result = call;
                        if (_disposed && _client != null) { Marshal.ReleaseComObject(_client); _client = null; }
                    }
                    catch (Exception e) { _result = e.HResult; }
                    finally { FreeParameters(); _completion.TrySetResult(true); }
                }
                return 0;
            }

            internal IAudioClient Take()
            {
                lock (_gate)
                {
                    Marshal.ThrowExceptionForHR(_result);
                    IAudioClient client = _client as IAudioClient;
                    if (client == null) throw new InvalidOperationException("Windows returned no process audio client.");
                    _client = null;
                    return client;
                }
            }

            void FreeParameters()
            {
                if (_variant != IntPtr.Zero) Marshal.FreeHGlobal(_variant);
                if (_parameters != IntPtr.Zero) Marshal.FreeHGlobal(_parameters);
                _variant = _parameters = IntPtr.Zero;
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    _disposed = true;
                    if (_client != null) { Marshal.ReleaseComObject(_client); _client = null; }
                }
            }
        }

        [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, IntPtr parameters,
            ICompletionHandler handler, out IActivationOperation operation);

        [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICompletionHandler { [PreserveSig] int ActivateCompleted(IActivationOperation operation); }

        [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAgileObject { }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivationOperation { [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object client); }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        internal struct WaveFormat
        {
            public ushort Tag, Channels;
            public int Rate, BytesPerSecond;
            public ushort Block, Bits, Extra;
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioClient
        {
            [PreserveSig] int Initialize(int mode, uint flags, long duration, long period, ref WaveFormat format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong timestamp);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
