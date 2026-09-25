using System;
using System.Globalization;
using System.Threading;

namespace VPB
{
    internal static class VpbNumberText
    {
        internal sealed class CultureScope : IDisposable
        {
            private readonly Thread _thread;
            private readonly CultureInfo _previous;
            private bool _restored;

            internal CultureScope(CultureInfo culture)
            {
                _thread = Thread.CurrentThread;
                _previous = _thread.CurrentCulture;
                if (ReferenceEquals(_previous, culture)) _restored = true;
                else _thread.CurrentCulture = culture;
            }

            public void Dispose()
            {
                if (_restored) return;
                _restored = true;
                if (ReferenceEquals(Thread.CurrentThread, _thread))
                    _thread.CurrentCulture = _previous;
            }
        }

        internal static CultureScope Invariant()
        {
            return new CultureScope(CultureInfo.InvariantCulture);
        }

        internal static string Format(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        internal static bool TryParseFloat(string text, out float value)
        {
            value = 0f;
            if (text == null) return false;
            string s = text.Trim();
            if (s.Length == 0) return false;
            bool hasDot = s.IndexOf('.') >= 0;
            bool hasComma = s.IndexOf(',') >= 0;
            if (hasDot && hasComma) return false;
            if (hasComma)
            {
                if (s.IndexOf(',') != s.LastIndexOf(',')) return false;
                s = s.Replace(',', '.');
            }
            float parsed;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (float.IsNaN(parsed) || float.IsInfinity(parsed)) return false;
            value = parsed;
            return true;
        }
    }
}
