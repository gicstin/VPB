# Multiplayer speech: developer notes

Player instructions: [Use speech in multiplayer](MultiplayerSpeech.md).

Default OSC reception is loopback port 9000, matching Voice Wizard's standard VRChat destination. TTS requests use its existing listener on port 4026. VPB uses an exclusive socket and retries when unavailable. VRChat and VPB speech must use the receiving port at separate times. Existing custom VPB configuration values are preserved.

## Boundaries

For an existing custom connection, open **Advanced connection settings** in VPB. **Receive port** matches Voice Wizard's OSC output port; **Wizard port** matches its listener port. **Wizard PID** selects a running copy when multiple copies are open; zero selects the only copy. Normal setup needs no changes here.

- Capture includes audio rendered by the selected Wizard process tree. It never falls back to desktop audio and never opens a microphone. Wizard owns microphone access for STT. Multiple Wizard playback outputs can duplicate audio; other sounds from that process are included.
- `/chatbox/input` has no final-result or origin identifier. VPB cannot distinguish final STT from drafts/media under arbitrary Wizard settings. The profile above is required. Blank messages, invalid UTF-8/control characters, unsupported OSC addresses and oversized messages are rejected. Received captions never enter local TTS.
- Typed requests allow 200 characters and 480 UTF-8 bytes; captions allow 480 UTF-8 bytes. Requests are limited to 5 per second, accepted OSC messages to 10 per second. No exact caption/audio synchronization or exactly-once synthesis claim is made.
- Audio plays from a separate streaming source at the peer avatar's HeadAudioSource, using its volume, mute and spatial settings. Scene audio queues remain intact. SpeechBlend uses the streaming source during received audio and restores its previous source afterward. Missing HeadAudioSource appears in status. Lip sync and scene-audio interaction require in-VaM validation.
- Audio uses PCM16 mono, 24 kHz, 20 ms frames. Active audio costs about 384 kbit/s before transport overhead. Zero frames are suppressed; sequence gaps retain silence. Buffers are bounded, missing frames produce silence, old local frames expire, and excess queued audio is dropped to control latency. These are format limits, not performance measurements.

## Privacy and compatibility

Speech channel 7 carries AES-256-GCM encrypted captions/audio on all transports. Session room key and fresh per-peer random salts derive directional keys; authenticated sequence numbers reject replay. Reliable captions and realtime audio have independent replay windows. Peer key confirmation is required and expires after three seconds without authenticated traffic. Unsupported peers receive no cleartext fallback. Existing pose/content channels are unchanged.

Use generated room codes and share them privately. This scheme depends on room-code secrecy and has no forward secrecy: somebody who later obtains the code and recorded handshake can derive past speech keys. Local OSC is unencrypted loopback UDP, so other local processes can inject messages. VPB binds its listener only to loopback; check Wizard's own listener/firewall settings separately.

VPB writes no speech recordings or transcript files and includes no transcript/audio content in logs. Wizard may retain text in its UI or send it through configured services; VPB cannot verify its settings or guarantee that the companion makes no internet requests. No Wizard binaries, engines, models or assets are redistributed.

## Verification

Run `VpbNet.exe --self-test-speech` for encryption, tampering/replay, independent caption delivery, OSC/UTF-8 bounds, PCM ordering, sequence wrap, short utterances, caption-mode changes and request feedback. OSC regressions use temporary loopback UDP ports. Checks do not open an audio device, launch Wizard, or launch VaM.

Connection status describes peer connectivity and local capture/listening. It does not confirm Wizard's TTS listener or playback. Typed requests have separate results with request IDs; duplicate requests are not executed twice. Results survive caption-mode changes and cannot be overwritten by periodic connection status. Caption switches keep capture running; pending OSC input is discarded before forwarding resumes.

Source review and compilation do not prove capture quality, lip sync or runtime latency. Before public release, exercise two VaM instances: speak in each direction, check unrelated audio exclusion, change avatars, disable sharing during speech, and reconnect. Confirm scene audio resumes and captions from an unavailable peer are not replayed. Measure latency, CPU and bandwidth during scene transfers.

Interface reference: [Wizard OSC listener](https://github.com/VRCWizard/TTS-Voice-Wizard/blob/623c442b4c3a2d6617a8dcd1520ee25e630c9966/OSCVRCWiz/Services/Integrations/OSCListener.cs), [Windows application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/).
