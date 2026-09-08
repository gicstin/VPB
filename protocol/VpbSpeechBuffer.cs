using System;
using System.Diagnostics;

namespace VpbNet
{
    public sealed class VpbSpeechBuffer
    {
        const int Slots = 16;
        readonly object _gate = new object();
        readonly float[][] _frames = new float[Slots][];
        readonly uint[] _sequence = new uint[Slots];
        readonly bool[] _present = new bool[Slots];
        uint _next, _highest;
        int _offset;
        bool _initialized, _playing;
        long _lastArrival, _waitingSince;

        public VpbSpeechBuffer()
        {
            for (int i = 0; i < Slots; i++) _frames[i] = new float[VpbNetSpeech.FrameSamples];
        }

        public bool Active
        {
            get { lock (_gate) return _initialized && Stopwatch.GetTimestamp() - _lastArrival < Stopwatch.Frequency / 3; }
        }

        public void Push(uint sequence, byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset > bytes.Length - VpbNetSpeech.FrameBytes) return;
            lock (_gate)
            {
                uint ahead = sequence - _next;
                if (_initialized && ahead >= 0x80000000u) return;
                if (!_initialized || ahead >= Slots)
                {
                    ClearLocked();
                    _next = _highest = sequence;
                    _initialized = true;
                    _waitingSince = Stopwatch.GetTimestamp();
                }
                int slot = (int)(sequence % Slots);
                if (_present[slot] && _sequence[slot] == sequence) return;
                for (int i = 0; i < VpbNetSpeech.FrameSamples; i++)
                {
                    int at = offset + i * 2;
                    _frames[slot][i] = unchecked((short)(bytes[at] | bytes[at + 1] << 8)) / 32768f;
                }
                _sequence[slot] = sequence;
                _present[slot] = true;
                if (sequence - _highest < 0x80000000u) _highest = sequence;
                _lastArrival = Stopwatch.GetTimestamp();
            }
        }

        public void Read(float[] destination)
        {
            Array.Clear(destination, 0, destination.Length);
            lock (_gate)
            {
                if (!_initialized) return;
                if (Stopwatch.GetTimestamp() - _lastArrival > Stopwatch.Frequency / 3) { ClearLocked(); return; }
                uint depth = _highest - _next;
                if (depth < 0x80000000u && depth > 7)
                {
                    _next = _highest - 3;
                    _offset = 0;
                }
                if (!_playing && _highest - _next < 2 && Stopwatch.GetTimestamp() - _waitingSince < Stopwatch.Frequency * 60 / 1000) return;
                _playing = true;
                for (int i = 0; i < destination.Length; i++)
                {
                    int slot = (int)(_next % Slots);
                    bool present = _present[slot] && _sequence[slot] == _next;
                    if (!present && _highest - _next >= 0x80000000u)
                    {
                        _playing = false;
                        _waitingSince = Stopwatch.GetTimestamp();
                        return;
                    }
                    if (present) destination[i] = _frames[slot][_offset];
                    if (++_offset == VpbNetSpeech.FrameSamples)
                    {
                        _present[slot] = false;
                        Array.Clear(_frames[slot], 0, _frames[slot].Length);
                        _offset = 0;
                        _next++;
                    }
                }
            }
        }

        public void Clear() { lock (_gate) ClearLocked(); }

        void ClearLocked()
        {
            for (int i = 0; i < Slots; i++)
            {
                _present[i] = false;
                Array.Clear(_frames[i], 0, _frames[i].Length);
            }
            _offset = 0;
            _initialized = _playing = false;
        }
    }
}
