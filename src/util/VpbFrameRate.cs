using UnityEngine;

namespace VPB
{
    // Real frame rate from frames counted over a wall-clock window (Time.realtimeSinceStartup).
    static class VpbFrameRate
    {
        public static float Current { get; private set; }

        const float WindowSeconds = 0.5f;
        static int _frames;
        static float _windowStart;
        static bool _inited;

        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (!_inited)
            {
                _inited = true;
                _windowStart = now;
                _frames = 0;
                return;
            }

            _frames++;
            float elapsed = now - _windowStart;
            if (elapsed >= WindowSeconds)
            {
                Current = _frames / elapsed;
                _frames = 0;
                _windowStart = now;
            }
        }
    }
}
