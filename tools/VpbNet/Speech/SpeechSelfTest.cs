using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VpbNet.Speech
{
    internal static class SpeechSelfTest
    {
        public static int RunConsole()
        {
            try
            {
                byte[] key = new byte[32];
                key[0] = 42;
                using (SpeechCipher a = new SpeechCipher(key))
                using (SpeechCipher b = new SpeechCipher(key))
                using (SpeechCipher stranger = new SpeechCipher(new byte[32]))
                {
                    Action<byte[], int> toB = delegate(byte[] bytes, int count) { byte[] plain; int n; b.Receive(bytes, count, out plain, out n); };
                    Action<byte[], int> toA = delegate(byte[] bytes, int count) { byte[] plain; int n; a.Receive(bytes, count, out plain, out n); };
                    a.Tick(0, toB);
                    b.Tick(0, toA);
                    a.Tick(1, toB);
                    Require(a.Ready && b.Ready, "mutual key confirmation");
                    var packets = new List<byte[]>();
                    Action<byte[], int> collect = delegate(byte[] bytes, int count)
                    {
                        byte[] copy = new byte[count];
                        Buffer.BlockCopy(bytes, 0, copy, 0, count);
                        packets.Add(copy);
                    };
                    byte[] caption = { VpbNetSpeech.Caption, 1, 0, 0, 0, (byte)'x' };
                    a.Send(caption, caption.Length, collect);
                    byte[] delayedCaption = packets[0];
                    byte[] frame = new byte[9 + VpbNetSpeech.FrameBytes];
                    frame[0] = VpbNetSpeech.Audio;
                    frame[9] = 123;
                    byte[] plain;
                    int length;
                    for (int i = 0; i < 100; i++)
                    {
                        packets.Clear();
                        Require(a.Send(frame, frame.Length, collect), "audio send");
                        byte[] wire = packets[0];
                        Require(wire.Length <= VpbIpc.MaxDataPayload, "audio fits transport");
                        byte[] corrupt = (byte[])wire.Clone();
                        corrupt[corrupt.Length - 1] ^= 1;
                        Require(!b.Receive(corrupt, corrupt.Length, out plain, out length), "tamper rejected");
                        Require(b.Receive(wire, wire.Length, out plain, out length) && length == frame.Length && plain[9] == 123, "audio round trip");
                        Require(!b.Receive(wire, wire.Length, out plain, out length), "replay rejected");
                        Require(!a.Receive(wire, wire.Length, out plain, out length), "reflection rejected");
                        Require(!stranger.Receive(wire, wire.Length, out plain, out length), "wrong room rejected");
                    }
                    Require(b.Receive(delayedCaption, delayedCaption.Length, out plain, out length) && plain[5] == 'x', "caption survives audio window");
                    Require(!b.Receive(delayedCaption, delayedCaption.Length, out plain, out length), "caption replay rejected");
                    for (int i = 0; i < delayedCaption.Length; i++)
                        Require(!b.Receive(delayedCaption, i, out plain, out length), "truncated cipher rejected");
                    packets.Clear();
                    b.Send(caption, caption.Length, collect);
                    Require(a.Receive(packets[0], packets[0].Length, out plain, out length), "reverse direction");
                }

                byte[] output = new byte[1024];
                Require(VpbNetSpeech.WriteText(output, VpbNetSpeech.Speak, new string('x', 480)) == 481, "UTF-8 byte boundary");
                Require(VpbNetSpeech.WriteText(output, VpbNetSpeech.Speak, new string('\u20ac', 200)) == 0, "multibyte bound");
                Require(VpbNetSpeech.WriteText(output, VpbNetSpeech.Speak, "line\nbreak") == 0, "control rejected");
                string text;
                Require(!VpbNetSpeech.TryText(new byte[] { 0xff }, 0, 1, out text), "invalid UTF-8");
                byte[] osc = Osc("/chatbox/input", ",sTF", "same words");
                Require(VoiceWizardOsc.TryCaption(osc, osc.Length, out text) && text == "same words", "OSC final text");
                Require(VoiceWizardOsc.TryCaption(osc, osc.Length, out text), "intentional repeated caption");
                for (int i = 0; i < osc.Length; i++) Require(!VoiceWizardOsc.TryCaption(osc, i, out text), "truncated OSC");
                foreach (byte[] bad in new[] { Osc("/chatbox/typing", ",sTF", "x"), Osc("/chatbox/input", ",sss", "x"), Osc("/chatbox/input", ",sTT", " ") })
                    Require(!VoiceWizardOsc.TryCaption(bad, bad.Length, out text), "OSC boundary");

                byte[] pcm = new byte[VpbNetSpeech.FrameBytes];
                for (int i = 1; i < pcm.Length; i += 2) pcm[i] = 64;
                var buffer = new VpbSpeechBuffer();
                buffer.Push(10, pcm, 0);
                buffer.Push(12, pcm, 0);
                buffer.Push(11, pcm, 0);
                float[] samples = new float[VpbNetSpeech.FrameSamples * 3];
                buffer.Read(samples);
                foreach (float sample in samples) Require(sample == 0.5f, "reordered PCM");
                buffer.Clear();
                buffer.Read(samples);
                foreach (float sample in samples) Require(sample == 0f, "clear silences");
                buffer.Push(20, pcm, 0);
                buffer.Read(samples);
                Require(samples[0] == 0f, "startup jitter wait");
                Thread.Sleep(75);
                buffer.Read(samples);
                Require(samples[0] == 0.5f && samples[VpbNetSpeech.FrameSamples] == 0f, "short utterance drains");
                buffer.Clear();
                buffer.Push(uint.MaxValue - 1, pcm, 0);
                buffer.Push(uint.MaxValue, pcm, 0);
                buffer.Push(0, pcm, 0);
                buffer.Read(samples);
                foreach (float sample in samples) Require(sample == 0.5f, "sequence wrap");
                buffer.Clear();
                Require(!buffer.Active, "inactive after clear");
                CheckOscReception();
                BrokerHost.RunSpeechControlSelfTest();
                Console.WriteLine("Speech checks passed: encryption, parser bounds, PCM, mixed OSC traffic, caption toggles and request feedback.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }

        static void CheckOscReception()
        {
            int port = FreePort();
            using (var bridge = new VoiceWizardOsc(port, VpbNetSpeech.DefaultWizardPort))
            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                byte[] noise = Osc("/avatar/parameters/test", ",sTF", "noise");
                byte[] words = Osc("/chatbox/input", ",sTF", "words");
                for (int i = 0; i < 12; i++) sender.SendTo(noise, endpoint);
                sender.SendTo(words, endpoint);
                int captions = 0;
                Action<string> receive = delegate(string text) { captions++; };
                for (int i = 0; i < 20 && captions == 0; i++) { bridge.Poll(0, receive); Thread.Sleep(1); }
                Require(captions == 1, "unrelated OSC must not consume caption quota");
                for (int i = 0; i < 12; i++) sender.SendTo(words, endpoint);
                for (int i = 0; i < 20; i++) { bridge.Poll(0, receive); Thread.Sleep(1); }
                Require(captions == 10, "caption rate remains bounded");
                sender.SendTo(words, endpoint);
                for (int i = 0; i < 20 && captions == 10; i++) { bridge.Poll(1000, receive); Thread.Sleep(1); }
                Require(captions == 11, "caption quota recovers next second");
            }
        }

        internal static int FreePort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        static byte[] Osc(params string[] values)
        {
            var bytes = new List<byte>();
            foreach (string value in values)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value));
                bytes.Add(0);
                while (bytes.Count % 4 != 0) bytes.Add(0);
            }
            return bytes.ToArray();
        }

        internal static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("Speech check failed: " + check);
        }
    }
}

namespace VpbNet
{
    public sealed partial class BrokerHost
    {
        internal static void RunSpeechControlSelfTest()
        {
            var host = new BrokerHost(1, 0, new byte[VpbIpc.SecretSize]);
            try
            {
                host._speechRoomKey = new byte[32];
                host.SpeechPeerEvent(1, Transport.PeerEventKind.Up);
                int port = Speech.SpeechSelfTest.FreePort();
                int at = VpbIpc.HeaderSize;
                host._rx[at] = VpbNetSpeech.Configure;
                host._rx[at + 1] = host._rx[at + 2] = 1;
                VpbIpc.WriteU16(host._rx, at + 4, 1);
                VpbIpc.WriteU16(host._rx, at + 6, port);
                VpbIpc.WriteU16(host._rx, at + 8, VpbNetSpeech.DefaultWizardPort);
                VpbIpc.WriteU32(host._rx, at + 14, 1);
                VpbIpc.WriteU32(host._rx, at + 18, 1);
                host.HandleSpeechCommand(22);
                host._speechOsc = new Speech.VoiceWizardOsc(port, VpbNetSpeech.DefaultWizardPort);
                Speech.VoiceWizardOsc connection = host._speechOsc;
                host._rx[at + 3] = 1;
                VpbIpc.WriteU32(host._rx, at + 18, 2);
                host.HandleSpeechCommand(22);
                Speech.SpeechSelfTest.Require(ReferenceEquals(connection, host._speechOsc) && host._speechTransmit && host._speechCaptions,
                    "enabling captions preserves voice connection");
                Speech.SpeechSelfTest.Require(host._speechDiscardCaptions, "discard captions queued before enabling");
                host.OnSpeechCaption("queued");
                Speech.SpeechSelfTest.Require(!host._speechCaptionObserved, "queued captions do not confirm reception");
                host._speechDiscardCaptions = false;
                host.OnSpeechCaption("current");
                host.OnSpeechCaption("next");
                host.HandleSpeechCommand(22);
                Speech.SpeechSelfTest.Require(host._speechCaptionObserved, "caption evidence survives traffic and configuration heartbeat");
                host._rx[at + 3] = 0;
                VpbIpc.WriteU32(host._rx, at + 18, 3);
                host.HandleSpeechCommand(22);
                Speech.SpeechSelfTest.Require(ReferenceEquals(connection, host._speechOsc) && host._speechTransmit && !host._speechCaptions,
                    "disabling captions preserves voice connection");
                Speech.SpeechSelfTest.Require(!host._speechCaptionObserved, "disabling captions resets reception evidence");
                host._speechAudioObserved = true;
                host.StopSpeechCapture();
                Speech.SpeechSelfTest.Require(!host._speechAudioObserved, "capture stop resets audio evidence");
                host._rx[at + 2] = 0;
                VpbIpc.WriteU32(host._rx, at + 18, 4);
                host.HandleSpeechCommand(22);
                Speech.SpeechSelfTest.Require(host._speechOsc == null, "stopping both outputs releases OSC port");
                byte[] configuration = new byte[22];
                Buffer.BlockCopy(host._rx, at, configuration, 0, configuration.Length);
                host._rx[at] = VpbNetSpeech.Speak;
                VpbIpc.WriteU32(host._rx, at + 1, 77);
                host._rx[at + 5] = (byte)'x';
                host.HandleSpeechCommand(6);
                string result = host._speechResult;
                Speech.SpeechSelfTest.Require(result.Length > 0 && host._speechRequestId == 77, "rejection identifies request");
                host.TickSpeech(1000);
                Speech.SpeechSelfTest.Require(host._speechResult == result && host._speechStatus != result,
                    "connection refresh preserves request failure");
                int requests = host._speechCommands;
                host.HandleSpeechCommand(6);
                Speech.SpeechSelfTest.Require(host._speechCommands == requests, "duplicate request is not executed twice");
                Buffer.BlockCopy(configuration, 0, host._rx, at, configuration.Length);
                host._rx[at + 3] = 1;
                VpbIpc.WriteU32(host._rx, at + 18, 5);
                host.HandleSpeechCommand(22);
                Speech.SpeechSelfTest.Require(host._speechResult == result && host._speechRequestId == 77,
                    "caption toggle preserves pending request result");
            }
            finally { host.CloseSpeech(); }
        }
    }
}
