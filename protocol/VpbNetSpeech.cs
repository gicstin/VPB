using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetSpeech
    {
        public const byte Channel = 7;
        public const byte Version = 1;
        public const byte Configure = 1;
        public const byte Speak = 2;
        public const byte Caption = 3;
        public const byte Audio = 4;
        public const byte Status = 5;
        public const byte RequestResult = 6;
        public const int SampleRate = 24000;
        public const int FrameSamples = 480;
        public const int FrameBytes = FrameSamples * 2;
        public const int MaxTextBytes = 480;
        public const int MaxPlainBytes = 992;
        public const int DefaultReceivePort = 9000;
        public const int DefaultWizardPort = 4026;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryText(byte[] data, int offset, int count, out string text)
        {
            text = null;
            if (data == null || offset < 0 || count < 1 || count > MaxTextBytes || offset > data.Length - count) return false;
            try { text = Utf8.GetString(data, offset, count); }
            catch (DecoderFallbackException) { return false; }
            if (text.Trim().Length == 0) return false;
            for (int i = 0; i < text.Length; i++)
                if (char.IsControl(text[i])) { text = null; return false; }
            return true;
        }

        public static int WriteText(byte[] target, byte kind, string text)
        {
            if (target == null || text == null || text.Length == 0 || text.Length > MaxTextBytes) return 0;
            for (int i = 0; i < text.Length; i++) if (char.IsControl(text[i])) return 0;
            try
            {
                int count = Utf8.GetByteCount(text);
                if (count < 1 || count > MaxTextBytes || count + 1 > target.Length || text.Trim().Length == 0) return 0;
                target[0] = kind;
                Utf8.GetBytes(text, 0, text.Length, target, 1);
                return count + 1;
            }
            catch (EncoderFallbackException) { return 0; }
        }
    }
}
