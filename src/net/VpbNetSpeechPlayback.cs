using System;
using UnityEngine;
using VpbNet;

namespace VPB
{
    internal sealed class VpbNetSpeechPlayback
    {
        readonly VpbSpeechBuffer _buffer = new VpbSpeechBuffer();
        GameObject _host;
        AudioSource _source;
        AudioSource _head;
        AudioClip _clip;
        SpeechBlend _blend;
        AudioSource _previousBlendSource;
        bool _previousLiveMode;
        string _avatar;
        float _retryAfter;

        public string Error { get; private set; }

        public void Push(string avatar, uint sequence, byte[] bytes, int offset)
        {
            if (string.IsNullOrEmpty(avatar)) return;
            if (_avatar != avatar) { Stop(); _avatar = avatar; }
            if (_source == null)
            {
                if (Time.realtimeSinceStartup < _retryAfter) return;
                if (!Attach(avatar))
                {
                    _avatar = avatar;
                    _retryAfter = Time.realtimeSinceStartup + 2f;
                    return;
                }
                _retryAfter = 0f;
            }
            _buffer.Push(sequence, bytes, offset);
            if (!_source.isPlaying) _source.Play();
        }

        bool Attach(string avatar)
        {
            try
            {
                Atom atom = SuperController.singleton == null ? null : SuperController.singleton.GetAtomByUid(avatar);
                AudioSourceControl control = atom == null ? null : atom.GetStorableByID("HeadAudioSource") as AudioSourceControl;
                if (control == null || control.audioSource == null) { Error = "Peer avatar has no HeadAudioSource"; return false; }
                _head = control.audioSource;
                _host = new GameObject("VPB_PeerSpeech");
                _host.transform.SetParent(_head.transform, false);
                _source = _host.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.loop = true;
                _source.pitch = 1f;
                _source.outputAudioMixerGroup = _head.outputAudioMixerGroup;
                _source.rolloffMode = _head.rolloffMode;
                _source.dopplerLevel = _head.dopplerLevel;
                _source.spread = _head.spread;
                _source.priority = _head.priority;
                if (_head.rolloffMode == AudioRolloffMode.Custom)
                    _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _head.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                _clip = AudioClip.Create("VPB peer speech", VpbNetSpeech.SampleRate, 1, VpbNetSpeech.SampleRate, true, _buffer.Read);
                _source.clip = _clip;
                _blend = atom.GetComponentInChildren<SpeechBlend>();
                if (_blend != null)
                {
                    _previousBlendSource = _blend.voiceAudioSource;
                    _previousLiveMode = _blend.liveMode;
                    _blend.voiceAudioSource = _source;
                    _blend.liveMode = true;
                }
                _avatar = avatar;
                Error = null;
                SyncSettings();
                return true;
            }
            catch (Exception e)
            {
                Stop();
                Error = "HeadAudio playback failed (" + e.GetType().Name + ")";
                LogUtil.LogWarning("[VPB.Net] " + Error);
                return false;
            }
        }

        public void Tick(string avatar)
        {
            if (_avatar != avatar || (_host != null && _head == null)) { Stop(); return; }
            if (_source == null) return;
            if (!_buffer.Active) { Stop(); return; }
            SyncSettings();
        }

        void SyncSettings()
        {
            if (_head == null || _source == null) return;
            _source.volume = _head.volume;
            _source.mute = _head.mute;
            _source.spatialBlend = _head.spatialBlend;
            _source.minDistance = _head.minDistance;
            _source.maxDistance = _head.maxDistance;
        }

        public void Stop()
        {
            _retryAfter = 0f;
            Error = null;
            _buffer.Clear();
            if (_blend != null && _blend.voiceAudioSource == _source)
            {
                _blend.voiceAudioSource = _previousBlendSource;
                if (_blend.liveMode) _blend.liveMode = _previousLiveMode;
            }
            if (_source != null) _source.Stop();
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _host = null;
            _source = _head = null;
            _clip = null;
            _blend = null;
            _previousBlendSource = null;
            _avatar = null;
        }
    }
}
