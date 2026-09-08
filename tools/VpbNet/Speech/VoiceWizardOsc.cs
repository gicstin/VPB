using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpbNet.Speech
{
    internal sealed class VoiceWizardOsc : IDisposable
    {
        readonly Socket _socket;
        readonly IPEndPoint _wizard;
        readonly byte[] _receive = new byte[1024];
        readonly byte[] _send = new byte[1024];
        long _window;
        int _messages;

        public VoiceWizardOsc(int receivePort, int wizardPort)
        {
            if (receivePort < 1024 || receivePort > 65535 || wizardPort < 1024 || wizardPort > 65535 || receivePort == wizardPort)
                throw new ArgumentOutOfRangeException("OSC ports must be different and between 1024 and 65535.");
            _wizard = new IPEndPoint(IPAddress.Loopback, wizardPort);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                _socket.ExclusiveAddressUse = true;
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, receivePort));
                _socket.Blocking = false;
            }
            catch { _socket.Dispose(); throw; }
        }

        public bool Poll(long now, Action<string> caption)
        {
            if (now - _window >= 1000) { _window = now; _messages = 0; }
            EndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
            for (int i = 0; i < 16; i++)
            {
                int count;
                try { count = _socket.ReceiveFrom(_receive, ref sender); }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock) return true;
                    if (e.SocketErrorCode == SocketError.MessageSize || e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    throw;
                }
                IPEndPoint endpoint = sender as IPEndPoint;
                if (endpoint == null || !IPAddress.IsLoopback(endpoint.Address)) continue;
                string text;
                if (TryCaption(_receive, count, out text) && _messages < 10)
                {
                    _messages++;
                    caption(text);
                }
            }
            return false;
        }

        internal static bool TryCaption(byte[] bytes, int count, out string text)
        {
            text = null;
            if (bytes == null || count < 1 || count > bytes.Length || count > 1024) return false;
            int offset = 0;
            string address, types;
            if (!ReadString(bytes, count, ref offset, out address) || address != "/chatbox/input") return false;
            if (!ReadString(bytes, count, ref offset, out types) || types.Length != 4 || types[0] != ',' || types[1] != 's'
                || (types[2] != 'T' && types[2] != 'F') || (types[3] != 'T' && types[3] != 'F')) return false;
            int start = offset;
            string ignored;
            if (!ReadString(bytes, count, ref offset, out ignored) || offset != count) return false;
            int end = start;
            while (end < count && bytes[end] != 0) end++;
            return VpbNetSpeech.TryText(bytes, start, end - start, out text);
        }

        static bool ReadString(byte[] bytes, int count, ref int offset, out string value)
        {
            value = null;
            int start = offset;
            while (offset < count && bytes[offset] != 0) offset++;
            if (offset == count) return false;
            try { value = VpbNetSpeech.Utf8.GetString(bytes, start, offset - start); }
            catch (DecoderFallbackException) { return false; }
            int padded = (offset + 4) & ~3;
            if (padded > count) return false;
            while (offset < padded) if (bytes[offset++] != 0) return false;
            return true;
        }

        public void Speak(string text)
        {
            if (text == null || text.Length > 200) throw new ArgumentException("TTS text is limited to 200 characters.");
            int offset = WriteString(_send, 0, "/TTSVoiceWizard/TextToSpeech");
            offset = WriteString(_send, offset, ",sFF");
            offset = WriteString(_send, offset, text);
            _socket.SendTo(_send, 0, offset, SocketFlags.None, _wizard);
        }

        static int WriteString(byte[] bytes, int offset, string value)
        {
            int count = VpbNetSpeech.Utf8.GetBytes(value, 0, value.Length, bytes, offset);
            int end = (offset + count + 4) & ~3;
            Array.Clear(bytes, offset + count, end - offset - count);
            return end;
        }

        public void Dispose() { _socket.Dispose(); }
    }
}
