# Use speech in multiplayer

**Listening or reading captions only?** You need VPB only. Skip to “Connect in VPB.”

To send speech, install [TTS Voice Wizard](https://github.com/VRCWizard/TTS-Voice-Wizard) and keep it open.

Voice Wizard cannot be used with VaM while VRChat is open.

## Set up Voice Wizard

1. Choose your voice and local/offline speech services. Test playback in Voice Wizard.
2. For microphone input, select your microphone and start speech recognition. For a different spoken voice, also enable speech-to-speech.
3. For typed messages from VPB, open **Integrations → Heartrate + OSC Listener** in Voice Wizard. Click **Activate OSC Listener** and enable **Activate OSC Listener on Start**. Leave receive port **4026** unchanged.
4. Turn off typing drafts, partial recognition, media output, OSC forwarding and smart splitting. Use one playback output.

Keep ChatGPT, translation, VoiceCommands and file exports off for this setup.

Sharing voice requires Windows build **20348 or newer**. Check with **Win+R**, then `winver`.

## Connect in VPB

Both people:

1. Use the updated VPB and join the same multiplayer room.
2. Load matching scenes and choose the Person you control.
3. Open **Speech** in the multiplayer panel and set **Speech: ON**.

## Send speech

| What you want | What to do |
| --- | --- |
| Type to speak (TTS) | Enable **Share voice**, wait for audio capture to become active, type in VPB and click **Speak** |
| Microphone to captions (STT) | Enable **STT captions**, then speak using Voice Wizard |
| Microphone to chosen voice (STTS) | Enable **Share voice** with speech-to-speech running in Voice Wizard. Enable **STT captions** too if wanted |

Typed messages also send a caption. Text stays in the field; replace it for your next message.

**Speak requires a connected partner and both avatars claimed.** To test your voice before someone joins, type directly in Voice Wizard.

## Stop sharing

Set **Share voice: OFF** for voice and **STT captions: OFF** for text. Disable speech or leave the room to stop both. **Closing the panel does not stop sharing.**

After reconnecting or changing Persons/scenes, turn sharing on again.

## Problems

| Problem | Fix |
| --- | --- |
| Waiting for scene, Person or peer | Check both players completed “Connect in VPB” |
| Voice Wizard not found | Open one copy of Voice Wizard |
| No voice heard | Check **Share voice**, the result below **Speak**, and the receiving Person's head-audio volume/mute |
| Drafts or media titles appear | Turn off those outputs in Voice Wizard |
| Speech port unavailable | Close VRChat or another OSC listener; VPB retries automatically |

[Advanced settings and developer notes](MultiplayerSpeechTechnical.md)
