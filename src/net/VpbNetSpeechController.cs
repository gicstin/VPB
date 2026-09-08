using System;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    public static class VpbNetSpeechController
    {
        static readonly byte[] Packet = new byte[VpbIpc.MaxDataPayload];
        static readonly VpbNetSpeechPlayback Playback = new VpbNetSpeechPlayback();
        static VpbNetUiKit.Chip _enableButton, _shareButton, _captionButton;
        static VpbNetUiKit.Chip _speakButton;
        static Text _statusLabel, _captionLabel, _resultLabel;
        static Text _wizardLabel;
        static string _wizardStatus;
        static float _nextWizardProbe;
        static InputField _text;
        static GameObject _details, _setup;
        static bool _showSetup;
        static bool _share, _captions, _connected, _configured;
        static int _peer = -1;
        static uint _claim, _epoch = 1;
        static float _nextConfigure, _captionUntil;
        static string _status = "Speech off";
        static string _loggedStatus;
        static bool _loggedPeerAudio;
        static string _caption = string.Empty;
        static string _requestResult = string.Empty;
        static uint _requestId;
        static float _requestDeadline;

        static bool Enabled
        {
            get { return Settings.Instance != null && Settings.Instance.NetSpeechEnabled != null && Settings.Instance.NetSpeechEnabled.Value; }
        }

        public static void BuildPanel(GameObject parent, float scale)
        {
            GameObject card = VpbNetUiKit.Card(parent, scale);
            VpbNetUiKit.SectionHeader(card, "Speech", scale, false);
            GameObject controls = VpbNetUiKit.Row(card, VpbNetUiKit.ButtonRef, scale);
            _enableButton = VpbNetUiKit.Btn(controls, "Enable speech", 0f, scale, ToggleEnabled);
            _shareButton = VpbNetUiKit.Btn(controls, "Share voice", 0f, scale, ToggleShare);
            _captionButton = VpbNetUiKit.Btn(controls, "STT captions", 0f, scale, ToggleCaptions);
            _statusLabel = VpbNetUiKit.Line(card, _status, VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, scale, true);
            _details = VpbNetUiKit.Pane(card, "Speech details", scale);
            _wizardLabel = VpbNetUiKit.Line(_details, string.Empty, VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef * 2, scale, true);
            _captionLabel = VpbNetUiKit.Line(_details, string.Empty, VpbNetUiKit.FontBody, UI.TextPrimary, VpbNetUiKit.LineRef * 2, scale, true);
            _captionLabel.supportRichText = false;
            GameObject entry = VpbNetUiKit.Row(_details, VpbNetUiKit.ButtonRef, scale);
            _text = VpbNetUiKit.Field(entry, "Type to speak (200 characters)", VpbNetUiKit.ButtonRef, scale);
            _text.characterLimit = 200;
            _speakButton = VpbNetUiKit.PrimaryBtn(entry, "Speak", 88f, scale, Speak);
            _resultLabel = VpbNetUiKit.Line(_details, string.Empty, VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef * 2, scale, true);
            _resultLabel.supportRichText = false;
            VpbNetUiKit.Btn(_details, "Advanced connection settings", 0f, scale, delegate { _showSetup = !_showSetup; RefreshUi(); });
            _setup = VpbNetUiKit.Pane(_details, "Speech setup", scale);
            VpbNetUiKit.Line(_setup,
                "Keep Voice Wizard's standard VRChat ports. Close VRChat before sharing in VPB. For typed TTS, enable Voice Wizard's OSC listener. Change these fields only to match an existing custom setup.",
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef * 3, scale, true);
            GameObject ports = VpbNetUiKit.Row(_setup, VpbNetUiKit.ButtonRef, scale);
            Settings settings = Settings.Instance;
            AddNumberField(ports, "Receive port", settings.NetSpeechOscPort.Value, scale, delegate(int value)
            {
                if (value < 1024 || value > 65535 || value == settings.NetSpeechWizardPort.Value) return false;
                settings.NetSpeechOscPort.Value = value;
                return true;
            });
            AddNumberField(ports, "Wizard port", settings.NetSpeechWizardPort.Value, scale, delegate(int value)
            {
                if (value < 1024 || value > 65535 || value == settings.NetSpeechOscPort.Value) return false;
                settings.NetSpeechWizardPort.Value = value;
                return true;
            });
            AddNumberField(ports, "Wizard PID (0 = auto)", settings.NetSpeechProcessId.Value, scale, delegate(int value)
            {
                if (value < 0) return false;
                settings.NetSpeechProcessId.Value = value;
                return true;
            });
            RefreshUi();
        }

        static void AddNumberField(GameObject parent, string label, int current, float scale, Func<int, bool> save)
        {
            GameObject column = VpbNetUiKit.Pane(parent, label, scale);
            VpbNetUiKit.Line(column, label, VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, scale, false);
            InputField field = VpbNetUiKit.Field(column, label, VpbNetUiKit.ButtonRef, scale);
            field.characterLimit = 10;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.text = current.ToString();
            field.onEndEdit.AddListener(delegate(string value)
            {
                int number;
                if (int.TryParse(value, out number) && number == current) { field.text = current.ToString(); return; }
                if (int.TryParse(value, out number) && save(number)) { current = number; Reset(); }
                else _status = "Invalid " + label;
                field.text = current.ToString();
            });
        }

        static void ToggleEnabled()
        {
            Settings.Instance.NetSpeechEnabled.Value = !Enabled;
            Reset();
            LogSpeechInfo(Enabled ? "Enabled" : "Disabled");
            RefreshUi();
        }

        static void ToggleShare()
        {
            if (!Enabled || !_connected) return;
            _share = !_share;
            LogSpeechInfo(_share ? "Voice sharing on" : "Voice sharing off");
            ConfigureChanged();
            RefreshUi();
        }

        static void ToggleCaptions()
        {
            if (!Enabled || !_connected) return;
            _captions = !_captions;
            LogSpeechInfo(_captions ? "STT captions on" : "STT captions off");
            ConfigureChanged();
            RefreshUi();
        }

        static void Speak()
        {
            if (_text == null) return;
            if (!Enabled || !_connected)
            {
                _requestResult = !Enabled ? "Enable speech before speaking." : "Connect a partner, load matching scenes and claim both avatars before speaking.";
                RefreshUi();
                return;
            }
            if (++_requestId == 0) _requestId = 1;
            _requestDeadline = 0;
            int count = VpbNetSpeech.WriteText(Packet, VpbNetSpeech.Speak, _text.text);
            if (count == 0) { _requestResult = "Message too long or empty. Use a shorter message."; RefreshUi(); return; }
            if (!_share) { _requestResult = "Enable Share voice before speaking"; RefreshUi(); return; }
            Buffer.BlockCopy(Packet, 1, Packet, 5, count - 1);
            VpbIpc.WriteU32(Packet, 1, _requestId);
            bool sent = VpbNetBrokerLink.SendSpeech(Packet, count + 4);
            _requestResult = sent ? "Sending request..." : "Request could not be sent. Check the connection and try again.";
            if (sent) _requestDeadline = Time.realtimeSinceStartup + 3f;
        }

        public static void Tick()
        {
            if (Enabled && Time.realtimeSinceStartup >= _nextWizardProbe)
            {
                _nextWizardProbe = Time.realtimeSinceStartup + 2f;
                string status = VpbNetVoiceWizardProbe.Check(Settings.Instance.NetSpeechProcessId.Value, Settings.Instance.NetSpeechWizardPort.Value);
                if (_wizardStatus != status)
                {
                    _wizardStatus = status;
                    LogSpeechInfo(status);
                }
            }
            bool connected = VpbNetPresence.PeerUp && VpbNetPresence.PeerId > 0 && VpbNetPresence.ScenesMatch
                && !string.IsNullOrEmpty(VpbNetPresence.MyAvatar) && !string.IsNullOrEmpty(VpbNetPresence.PeerAvatar);
            int peer = VpbNetPresence.PeerId;
            uint claim = VpbNetPresence.ClaimRevision;
            if (_connected != connected || _peer != peer || _claim != claim)
            {
                Reset();
                _connected = connected;
                _peer = peer;
                _claim = claim;
            }
            bool enabled = Enabled && connected;
            if (!enabled)
            {
                if (_configured) SendConfiguration(false);
                Playback.Stop();
                _share = _captions = false;
                _status = !Enabled ? "Enable speech to use voice and captions"
                    : !VpbNetPresence.PeerUp || VpbNetPresence.PeerId <= 0 ? "Waiting for partner to join"
                    : !VpbNetPresence.ScenesMatch ? "Both players must load the same scene"
                    : "Both players must claim an avatar";
            }
            else if (Time.realtimeSinceStartup >= _nextConfigure)
            {
                _nextConfigure = Time.realtimeSinceStartup + 0.5f;
                SendConfiguration(true);
            }
            Playback.Tick(VpbNetPresence.PeerAvatar);
            if (_requestDeadline > 0 && Time.realtimeSinceStartup >= _requestDeadline)
            {
                _requestDeadline = 0;
                _requestResult = "No response to this request. Check the connection and try again.";
            }
            if (Playback.Error != null) _status = Playback.Error;
            if (_caption.Length > 0 && Time.realtimeSinceStartup > _captionUntil) _caption = string.Empty;
            RefreshUi();
        }

        static void SendConfiguration(bool enabled)
        {
            Settings settings = Settings.Instance;
            if (settings == null) return;
            if (enabled && !_configured) _status = "Waiting for broker speech support";
            int receivePort = settings.NetSpeechOscPort.Value, wizardPort = settings.NetSpeechWizardPort.Value;
            if (enabled && (receivePort < 1024 || receivePort > 65535 || wizardPort < 1024 || wizardPort > 65535
                || receivePort == wizardPort || settings.NetSpeechProcessId.Value < 0))
            {
                enabled = false;
                _status = "Invalid speech ports or process ID";
                _share = _captions = false;
                Playback.Stop();
            }
            Packet[0] = VpbNetSpeech.Configure;
            Packet[1] = enabled ? (byte)1 : (byte)0;
            Packet[2] = enabled && _share ? (byte)1 : (byte)0;
            Packet[3] = enabled && _captions ? (byte)1 : (byte)0;
            VpbIpc.WriteU16(Packet, 4, _peer > 0 ? _peer : 0);
            VpbIpc.WriteU16(Packet, 6, settings.NetSpeechOscPort.Value);
            VpbIpc.WriteU16(Packet, 8, settings.NetSpeechWizardPort.Value);
            VpbIpc.WriteU32(Packet, 10, unchecked((uint)settings.NetSpeechProcessId.Value));
            VpbIpc.WriteU32(Packet, 14, _claim);
            VpbIpc.WriteU32(Packet, 18, _epoch);
            if (VpbNetBrokerLink.SendSpeech(Packet, 22)) _configured = enabled;
        }

        public static void Receive(byte[] bytes, int offset, int count, bool stale)
        {
            if (count < 5 || VpbIpc.ReadU32(bytes, offset) != _epoch) return;
            offset += 4;
            count -= 4;
            byte kind = bytes[offset];
            string text;
            if (kind == VpbNetSpeech.Status && VpbNetSpeech.TryText(bytes, offset + 1, count - 1, out text)) _status = text;
            else if (kind == VpbNetSpeech.RequestResult && count > 5 && VpbIpc.ReadU32(bytes, offset + 1) == _requestId
                && VpbNetSpeech.TryText(bytes, offset + 5, count - 5, out text))
            {
                _requestDeadline = 0;
                _requestResult = text;
            }
            else if (Enabled && _connected && !stale && kind == VpbNetSpeech.Caption && count > 5
                && VpbIpc.ReadU32(bytes, offset + 1) == _claim && VpbNetSpeech.TryText(bytes, offset + 5, count - 5, out text))
            {
                _caption = text;
                _captionUntil = Time.realtimeSinceStartup + 12f;
            }
            else if (Enabled && _connected && !stale && kind == VpbNetSpeech.Audio && count == 9 + VpbNetSpeech.FrameBytes
                && VpbIpc.ReadU32(bytes, offset + 1) == _claim && VpbNetPresence.ScenesMatch)
            {
                if (!_loggedPeerAudio)
                {
                    _loggedPeerAudio = true;
                    LogSpeechInfo("Peer audio received");
                }
                Playback.Push(VpbNetPresence.PeerAvatar, VpbIpc.ReadU32(bytes, offset + 5), bytes, offset + 9);
            }
        }

        public static void Reset()
        {
            _loggedPeerAudio = false;
            _nextWizardProbe = 0f;
            if (!Enabled) _wizardStatus = null;
            _share = _captions = false;
            _caption = string.Empty;
            _requestResult = string.Empty;
            _requestDeadline = 0;
            Playback.Stop();
            if (++_epoch == 0) _epoch = 1;
            _nextConfigure = 0;
            if (_configured) SendConfiguration(false);
            _configured = false;
        }

        public static void DestroyUi()
        {
            _wizardLabel = null;
            _details = _setup = null;
            _enableButton = _shareButton = _captionButton = null;
            _speakButton = null;
            _statusLabel = _captionLabel = _resultLabel = null;
            _text = null;
        }

        static void ConfigureChanged()
        {
            if (++_epoch == 0) _epoch = 1;
            _nextConfigure = 0;
            SendConfiguration(Enabled && _connected);
        }

        static void LogSpeechInfo(string text)
        {
            string message = "[VPB.Net][Speech] " + text;
            VPB.src.util.VPBLogger.Main.LogInfo(message, false);
            if (SuperController.singleton != null)
                SuperController.singleton.Message(message, logToFile: false, splash: false);
        }

        static void RefreshUi()
        {
            if (_loggedStatus != _status)
            {
                _loggedStatus = _status;
                LogSpeechInfo(_status);
            }
            VpbNetUiKit.Show(_details, Enabled);
            if (_speakButton != null) _speakButton.SetEnabled(Enabled && _connected && _share);
            VpbNetUiKit.Show(_setup, Enabled && _showSetup);
            if (_captionLabel != null) VpbNetUiKit.Show(_captionLabel.gameObject, _caption.Length > 0);
            if (_resultLabel != null)
            {
                VpbNetUiKit.Show(_resultLabel.gameObject, _requestResult.Length > 0);
                if (_resultLabel.text != _requestResult) _resultLabel.text = _requestResult;
            }
            if (_enableButton != null)
            {
                _enableButton.SetText(Enabled ? "Speech: ON" : "Speech: OFF");
                _enableButton.SetRole(Enabled ? UI.AccentGreen : UI.ChromePanel, UI.TextPrimary);
            }
            if (_shareButton != null)
            {
                _shareButton.SetText(_share ? "Share voice: ON" : "Share voice: OFF");
                _shareButton.SetEnabled(Enabled && _connected);
                _shareButton.SetRole(_share ? UI.AccentGreen : UI.ChromePanel, UI.TextPrimary);
            }
            if (_captionButton != null)
            {
                _captionButton.SetText(_captions ? "STT captions: ON" : "STT captions: OFF");
                _captionButton.SetEnabled(Enabled && _connected);
                _captionButton.SetRole(_captions ? UI.AccentGreen : UI.ChromePanel, UI.TextPrimary);
            }
            if (_statusLabel != null && _statusLabel.text != _status) _statusLabel.text = _status;
            if (_wizardLabel != null && _wizardLabel.text != _wizardStatus) _wizardLabel.text = _wizardStatus ?? string.Empty;
            if (_captionLabel != null && _captionLabel.text != _caption) _captionLabel.text = _caption;
        }
    }
}
