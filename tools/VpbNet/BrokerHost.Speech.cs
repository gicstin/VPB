using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpbNet.Speech;
using VpbNet.Transport;

namespace VpbNet
{
    public sealed partial class BrokerHost
    {
        sealed class SpeechPeer : IDisposable
        {
            public SpeechCipher Cipher;
            public Action<byte[], int> SendWire;
            public Action<byte[], int> SendReliable;
            public bool Stalled;
            public long ReceiveWindow;
            public int Received;
            public void Dispose() { Cipher.Dispose(); }
        }

        readonly Dictionary<int, SpeechPeer> _speechPeers = new Dictionary<int, SpeechPeer>();
        readonly byte[] _speechPacket = new byte[VpbNetSpeech.MaxPlainBytes];
        readonly byte[] _speechIpc = new byte[VpbIpc.MaxDatagram];
        byte[] _speechRoomKey;
        VoiceWizardOsc _speechOsc;
        ProcessAudioCapture _speechCapture;
        bool _speechEnabled, _speechTransmit, _speechCaptions, _speechDiscardCaptions;
        bool _speechAudioObserved, _speechCaptionObserved;
        int _speechPeerId, _speechPid, _speechOscPort, _speechWizardPort;
        uint _speechClaim, _speechSequence, _speechEpoch;
        Action<string> _speechCaptionHandler;
        long _speechNextProbe, _speechNextStatus, _speechCommandWindow, _speechLease;
        int _speechCommands;
        string _speechStatus = "Speech off";
        string _speechResult = string.Empty;
        uint _speechRequestId;

        void SpeechPeerEvent(int peerId, PeerEventKind kind)
        {
            SpeechPeer peer;
            if (kind == PeerEventKind.Up || kind == PeerEventKind.Down)
            {
                if (_speechPeers.TryGetValue(peerId, out peer)) { peer.Dispose(); _speechPeers.Remove(peerId); }
                if (kind == PeerEventKind.Up && _speechRoomKey != null)
                {
                    peer = new SpeechPeer { Cipher = new SpeechCipher(_speechRoomKey) };
                    peer.SendWire = delegate(byte[] data, int count) { SendSpeechWire(peerId, data, count, false); };
                    peer.SendReliable = delegate(byte[] data, int count) { SendSpeechWire(peerId, data, count, true); };
                    _speechPeers.Add(peerId, peer);
                }
            }
            else if (_speechPeers.TryGetValue(peerId, out peer))
                peer.Stalled = kind == PeerEventKind.Stalled;
            if (peerId == _speechPeerId && (kind == PeerEventKind.Down || kind == PeerEventKind.Stalled))
            {
                _speechTransmit = _speechCaptions = false;
                ClearSpeechOutbound();
                StopSpeechCapture();
                _speechStatus = "Peer unavailable; sharing stopped";
            }
        }

        void SendSpeechWire(int peerId, byte[] data, int count, bool reliable)
        {
            if (_transport == null || !_speechEnabled || peerId != _speechPeerId) return;
            if (reliable)
            {
                if (QueuedFor(peerId) > 0 || !_transport.Send(peerId, data, 0, count, VpbNetSpeech.Channel, true))
                    QueueOutbound(peerId, data, 0, count, VpbNetSpeech.Channel);
            }
            else _transport.Send(peerId, data, 0, count, VpbNetSpeech.Channel, false);
        }

        void HandleSpeechCommand(int length)
        {
            int at = VpbIpc.HeaderSize;
            if (length < 1) return;
            byte command = _rx[at];
            long now = _clock.ElapsedMilliseconds;
            if (command == VpbNetSpeech.Configure)
            {
                if (length != 22 || _rx[at + 1] > 1 || _rx[at + 2] > 1 || _rx[at + 3] > 1) return;
                bool enabled = _rx[at + 1] != 0;
                bool transmit = _rx[at + 2] != 0;
                int peerId = VpbIpc.ReadU16(_rx, at + 4);
                int port = VpbIpc.ReadU16(_rx, at + 6);
                int wizard = VpbIpc.ReadU16(_rx, at + 8);
                int pid = unchecked((int)VpbIpc.ReadU32(_rx, at + 10));
                uint claim = VpbIpc.ReadU32(_rx, at + 14);
                uint epoch = VpbIpc.ReadU32(_rx, at + 18);
                if (epoch == 0 || (_speechEpoch != 0 && epoch != _speechEpoch && epoch - _speechEpoch >= 0x80000000u)) return;
                if (enabled && (port < 1024 || wizard < 1024 || port == wizard || pid < 0 || !_speechPeers.ContainsKey(peerId))) return;
                bool captions = enabled && _rx[at + 3] != 0;
                bool reset = !enabled || enabled != _speechEnabled || peerId != _speechPeerId || claim != _speechClaim || port != _speechOscPort
                    || wizard != _speechWizardPort || pid != _speechPid;
                if (reset || epoch != _speechEpoch)
                {
                    ClearSpeechOutbound();
                    if (reset) _speechResult = string.Empty;
                }
                if (captions != _speechCaptions) _speechDiscardCaptions = true;
                if (reset || captions != _speechCaptions) _speechCaptionObserved = false;
                if (reset)
                {
                    if (_speechOsc != null) { _speechOsc.Dispose(); _speechOsc = null; }
                }
                if (reset || transmit != _speechTransmit)
                {
                    StopSpeechCapture();
                    _speechNextProbe = 0;
                }
                if (!transmit && !captions && _speechOsc != null) { _speechOsc.Dispose(); _speechOsc = null; }
                _speechEnabled = enabled;
                _speechTransmit = enabled && transmit;
                _speechCaptions = captions;
                _speechPeerId = peerId;
                _speechOscPort = port;
                _speechWizardPort = wizard;
                _speechPid = pid;
                _speechClaim = claim;
                _speechEpoch = epoch;
                _speechLease = now + 2500;
                if (!enabled) _speechStatus = "Speech off";
                return;
            }
            if (command != VpbNetSpeech.Speak || length < 6) return;
            uint requestId = VpbIpc.ReadU32(_rx, at + 1);
            if (requestId == 0) return;
            if (requestId == _speechRequestId) { SendSpeechResult(); return; }
            if (_speechRequestId != 0 && requestId - _speechRequestId >= 0x80000000u) return;
            _speechRequestId = requestId;
            if (now - _speechCommandWindow >= 1000) { _speechCommandWindow = now; _speechCommands = 0; }
            if (++_speechCommands > 5) { SetSpeechResult("Too many requests. Wait a second and try again."); return; }
            if (!_speechEnabled) { SetSpeechResult("Enable speech before sending a message."); return; }
            SpeechPeer peer;
            if (!_speechPeers.TryGetValue(_speechPeerId, out peer) || peer.Stalled || !peer.Cipher.Ready)
            {
                SetSpeechResult("Your partner has not enabled speech yet.");
                return;
            }
            string text;
            if (!VpbNetSpeech.TryText(_rx, at + 5, length - 5, out text) || text.Length > 200)
            { SetSpeechResult("Message too long or invalid. Use a shorter message."); return; }
            if (!_speechTransmit || _speechOsc == null || _speechCapture == null || !_speechCapture.Started)
            {
                SetSpeechResult("Enable Share voice and wait for Voice Wizard capture to start.");
                return;
            }
            try
            {
                _speechOsc.Speak(text);
                SendSpeechCaption(text);
                SetSpeechResult("Sent to Voice Wizard. If silent, check its OSC listener.");
            }
            catch (Exception e)
            {
                SetSpeechResult("Request failed (" + e.GetType().Name + "). Check Voice Wizard's OSC listener and try again.");
                Log(1, "[Speech] " + _speechResult);
            }
        }

        void SendSpeechCaption(string text)
        {
            SpeechPeer peer;
            if (!_speechEnabled || !_speechPeers.TryGetValue(_speechPeerId, out peer) || peer.Stalled || !peer.Cipher.Ready) return;
            int length = VpbNetSpeech.WriteText(_speechPacket, VpbNetSpeech.Caption, text);
            if (length == 0) return;
            Buffer.BlockCopy(_speechPacket, 1, _speechPacket, 5, length - 1);
            VpbIpc.WriteU32(_speechPacket, 1, _speechClaim);
            peer.Cipher.Send(_speechPacket, length + 4, peer.SendReliable);
        }

        void TickSpeech(long now)
        {
            if (_speechEnabled && now > _speechLease)
            {
                _speechEnabled = _speechTransmit = false;
                ClearSpeechOutbound();
                StopSpeechCapture();
                if (_speechOsc != null) { _speechOsc.Dispose(); _speechOsc = null; }
                _speechStatus = "Speech stopped: plugin heartbeat expired";
            }
            if (_speechCapture != null && _speechCapture.Finished)
            {
                if (_speechCapture.Error != null)
                {
                    _speechStatus = _speechCapture.Error;
                    _speechNextProbe = now + 5000;
                }
                _speechCapture.Dispose();
                _speechCapture = null;
                _speechAudioObserved = false;
            }
            SpeechPeer peer;
            bool connected = _speechEnabled && _speechPeers.TryGetValue(_speechPeerId, out peer);
            if (connected)
            {
                peer = _speechPeers[_speechPeerId];
                if (!peer.Stalled) peer.Cipher.Tick(now, peer.SendWire);
                if (!peer.Cipher.Ready || peer.Stalled)
                {
                    StopSpeechCapture();
                    ClearSpeechOutbound();
                    if (_speechOsc != null) { _speechOsc.Dispose(); _speechOsc = null; }
                    _speechStatus = "Waiting for peer to enable encrypted speech";
                }
                else
                {
                    if ((_speechTransmit || _speechCaptions) && now >= _speechNextProbe)
                    {
                        _speechNextProbe = now + 2000;
                        try
                        {
                            if (_speechOsc == null)
                            {
                                _speechOsc = new VoiceWizardOsc(_speechOscPort, _speechWizardPort);
                                _speechCaptionObserved = false;
                            }
                            if (_speechTransmit && _speechCapture == null)
                            {
                                int pid = FindVoiceWizard();
                                if (pid > 0) _speechCapture = new ProcessAudioCapture(pid);
                                else _speechStatus = "Start TTSVoiceWizard; select its PID if multiple copies run";
                            }
                        }
                        catch (Exception e) { SpeechFailure("Voice Wizard connection", e); }
                    }
                    if (_speechOsc != null)
                    {
                        try
                        {
                            if (_speechCaptionHandler == null) _speechCaptionHandler = OnSpeechCaption;
                            bool drained = _speechOsc.Poll(now, _speechCaptionHandler);
                            if (drained) _speechDiscardCaptions = false;
                        }
                        catch (Exception e)
                        {
                            SpeechFailure("Voice Wizard OSC receive", e);
                            _speechOsc.Dispose();
                            _speechOsc = null;
                        }
                    }
                    if (_speechTransmit && _speechOsc != null && _speechCapture != null && _speechCapture.Started)
                    {
                        for (int i = 0; i < 6 && _speechCapture.TryRead(_speechPacket, 9); i++)
                        {
                            uint sequence = ++_speechSequence;
                            bool audible = false;
                            for (int j = 9; j < 9 + VpbNetSpeech.FrameBytes; j++)
                                if (_speechPacket[j] != 0) { audible = true; break; }
                            if (!audible) continue;
                            _speechAudioObserved = true;
                            _speechPacket[0] = VpbNetSpeech.Audio;
                            VpbIpc.WriteU32(_speechPacket, 1, _speechClaim);
                            VpbIpc.WriteU32(_speechPacket, 5, sequence);
                            peer.Cipher.Send(_speechPacket, 9 + VpbNetSpeech.FrameBytes, peer.SendWire);
                        }
                        _speechStatus = _speechAudioObserved
                            ? "Encrypted peer connected; Voice Wizard audio detected"
                            : "Encrypted peer connected; Voice Wizard capture ready, waiting for audio";
                        if (_speechCaptions && _speechCaptionObserved) _speechStatus += "; captions confirmed";
                    }
                    else if (!_speechTransmit && (!_speechCaptions || _speechOsc != null)) _speechStatus = _speechCaptions
                        ? (_speechCaptionObserved ? "Encrypted peer connected; Voice Wizard captions confirmed"
                            : "Encrypted peer connected; OSC listener open, waiting for Voice Wizard captions")
                        : "Encrypted peer connected; ready to receive speech";
                }
            }
            if (now >= _speechNextStatus)
            {
                _speechNextStatus = now + 1000;
                if (_bound)
                {
                    int count = VpbNetSpeech.WriteText(_speechPacket, VpbNetSpeech.Status, _speechStatus);
                    SendSpeechIpc(_speechPacket, count);
                    SendSpeechResult();
                }
            }
        }

        void OnSpeechCaption(string text)
        {
            if (!_speechCaptions || _speechDiscardCaptions) return;
            _speechCaptionObserved = true;
            SendSpeechCaption(text);
        }

        void SetSpeechResult(string text)
        {
            _speechResult = text;
            SendSpeechResult();
        }

        void SendSpeechResult()
        {
            if (_speechResult.Length == 0) return;
            int count = VpbNetSpeech.WriteText(_speechPacket, VpbNetSpeech.RequestResult, _speechResult);
            if (count == 0) return;
            Buffer.BlockCopy(_speechPacket, 1, _speechPacket, 5, count - 1);
            VpbIpc.WriteU32(_speechPacket, 1, _speechRequestId);
            SendSpeechIpc(_speechPacket, count + 4);
        }

        int FindVoiceWizard()
        {
            if (_speechPid > 0)
            {
                using (Process p = Process.GetProcessById(_speechPid))
                    return string.Equals(p.ProcessName, "TTSVoiceWizard", StringComparison.OrdinalIgnoreCase) ? p.Id : 0;
            }
            Process[] candidates = Process.GetProcessesByName("TTSVoiceWizard");
            try { return candidates.Length == 1 ? candidates[0].Id : 0; }
            finally { for (int i = 0; i < candidates.Length; i++) candidates[i].Dispose(); }
        }

        void ReceiveSpeech(int peerId, byte[] data, int count)
        {
            SpeechPeer peer;
            if (!_speechEnabled || peerId != _speechPeerId || !_speechPeers.TryGetValue(peerId, out peer) || peer.Stalled) return;
            long now = _clock.ElapsedMilliseconds;
            if (now - peer.ReceiveWindow >= 1000) { peer.ReceiveWindow = now; peer.Received = 0; }
            if (++peer.Received > 128) return;
            byte[] plain;
            int length;
            if (!peer.Cipher.Receive(data, count, out plain, out length)) return;
            if (plain[0] == VpbNetSpeech.Audio && length == 9 + VpbNetSpeech.FrameBytes)
            {
                if (VpbIpc.ReadU32(plain, 1) != _speechClaim) return;
                SendSpeechIpc(plain, length);
            }
            else if (plain[0] == VpbNetSpeech.Caption && length > 5 && VpbIpc.ReadU32(plain, 1) == _speechClaim)
            {
                string text;
                if (VpbNetSpeech.TryText(plain, 5, length - 5, out text)) SendSpeechIpc(plain, length);
            }
        }

        void SendSpeechIpc(byte[] bytes, int count)
        {
            if (!_bound || count < 1) return;
            int length = VpbIpc.WriteHeader(_speechIpc, VpbIpcMsg.Speech, NextSeq(), _token, count + 4);
            VpbIpc.WriteU32(_speechIpc, VpbIpc.HeaderSize, _speechEpoch);
            Buffer.BlockCopy(bytes, 0, _speechIpc, VpbIpc.HeaderSize + 4, count);
            try { _socket.SendTo(_speechIpc, 0, length, SocketFlags.None, _pluginEndpoint); }
            catch (SocketException) { }
        }

        void SpeechFailure(string operation, Exception error)
        {
            SocketException socketError = error as SocketException;
            bool portBusy = operation == "Voice Wizard connection" && _speechOsc == null && socketError != null
                && (socketError.SocketErrorCode == SocketError.AddressAlreadyInUse || socketError.SocketErrorCode == SocketError.AccessDenied);
            _speechStatus = portBusy
                ? "Speech port is unavailable. Close VRChat or another OSC listener; VPB retries automatically."
                : operation + " failed (" + error.GetType().Name + ")";
        }

        void StopSpeechCapture()
        {
            _speechAudioObserved = false;
            if (_speechCapture != null) _speechCapture.Dispose();
        }

        void CloseSpeech()
        {
            ClearSpeechOutbound();
            _speechEnabled = _speechTransmit = _speechCaptions = false;
            StopSpeechCapture();
            if (_speechOsc != null) { _speechOsc.Dispose(); _speechOsc = null; }
            foreach (SpeechPeer peer in _speechPeers.Values) peer.Dispose();
            _speechPeers.Clear();
            if (_speechRoomKey != null) CryptographicOperations.ZeroMemory(_speechRoomKey);
            _speechRoomKey = null;
            _speechStatus = "Speech off";
            _speechResult = string.Empty;
            _speechEpoch = 0;
        }

        void ClearSpeechOutbound()
        {
            for (int i = _outbound.Count - 1; i >= 0; i--)
            {
                Outbound item = _outbound[i];
                if (item.Channel != VpbNetSpeech.Channel) continue;
                Array.Clear(item.Buffer, 0, item.Length);
                _outboundFree.Enqueue(item);
                _outbound.RemoveAt(i);
            }
        }
    }
}
