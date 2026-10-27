using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameKaleidoscope
{
    public sealed partial class Plugin
    {
        private sealed class DuckSource
        {
            internal AudioSource Source;
            internal float BaseVolume;
            internal float AppliedVolume;
            internal bool HasApplied;
            internal AudioLowPassFilter LowPass;
            internal bool OwnsLowPass;
            internal bool Logged;
            internal bool LoggedInitial;
            internal bool LoggedNextFrame;
            internal int InitialFrame;
        }

        private readonly Dictionary<int, DuckSource> _duckSources = new Dictionary<int, DuckSource>();
        private readonly List<int> _deadDuckSources = new List<int>();
        private ConfigEntry<float> _worldAudioGain, _moanAudioGain, _audioFadeIn, _audioFadeOut;
        private ConfigEntry<float> _worldLowPassCutoff, _moanLowPassCutoff;
        private float _duckAmount;
        private float _nextAudioScan;
        private bool _audioDiagnosticStarted;
        private int _audioDiagnosticSources;
        private bool _audioDiagnosticNoSourcesLogged;

        private void InitializeAudioDucking()
        {
            _worldAudioGain = F("05.ThoughtAudio", "WorldGain", .6f, 0f, 1f, "思考中の現実音と通常ゲーム音の音量比。0でも通常音声の再生自体は継続する");
            _moanAudioGain = F("05.ThoughtAudio", "MoanGain", .65f, 0f, 1f, "思考中の喘ぎ声の音量比");
            _audioFadeIn = F("05.ThoughtAudio", "DuckInSeconds", .3f, 0f, 5f, "背景音を小さくする時間");
            _audioFadeOut = F("05.ThoughtAudio", "DuckOutSeconds", .6f, 0f, 5f, "背景音を戻す時間");
            _worldLowPassCutoff = F("05.ThoughtAudio", "WorldLowPassHz", 1600f, 500f, 22000f, "思考中の現実音と通常ゲーム音のローパス周波数");
            _moanLowPassCutoff = F("05.ThoughtAudio", "MoanLowPassHz", 22000f, 500f, 22000f, "思考中の喘ぎ声のローパス周波数");
        }

        private void UpdateAudioDucking(bool thought)
        {
            float duration = thought ? _audioFadeIn.Value : _audioFadeOut.Value;
            _duckAmount = duration <= .001f ? (thought ? 1f : 0f)
                : Mathf.MoveTowards(_duckAmount, thought ? 1f : 0f, Time.unscaledDeltaTime / duration);
            if (thought && !_audioDiagnosticStarted)
            {
                _audioDiagnosticStarted = true;
                _audioDiagnosticSources = 0;
                _audioDiagnosticNoSourcesLogged = false;
                Logger.LogInfo("Thought audio duck started: world=" + _worldAudioGain.Value
                    + " moan=" + _moanAudioGain.Value);
            }
            if (!thought && _audioDiagnosticStarted)
            {
                Logger.LogInfo("Thought audio duck ended: trackedSources=" + _audioDiagnosticSources);
                _audioDiagnosticStarted = false;
            }
            if (_duckAmount <= .0001f && _duckSources.Count == 0) return;

            if (_duckAmount > .0001f && Time.unscaledTime >= _nextAudioScan)
            {
                _nextAudioScan = Time.unscaledTime + .4f;
                foreach (AudioSource source in FindObjectsOfType<AudioSource>())
                {
                    if (source == null || source.gameObject.name == "MainGameVoiceFaceEventBridge.ExternalVoicePlayer") continue;
                    int id = source.GetInstanceID();
                    if (!_duckSources.ContainsKey(id))
                    {
                        AudioLowPassFilter lowPass = source.GetComponent<AudioLowPassFilter>();
                        bool ownsLowPass = lowPass == null;
                        if (ownsLowPass) lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
                        _duckSources[id] = new DuckSource { Source = source, BaseVolume = source.volume,
                            LowPass = lowPass, OwnsLowPass = ownsLowPass };
                    }
                }
                if (_duckSources.Count == 0 && !_audioDiagnosticNoSourcesLogged)
                {
                    Logger.LogWarning("Thought audio duck found no eligible AudioSource");
                    _audioDiagnosticNoSourcesLogged = true;
                }
            }

            _deadDuckSources.Clear();
            foreach (var pair in _duckSources)
            {
                DuckSource entry = pair.Value;
                if (entry.Source == null) { _deadDuckSources.Add(pair.Key); continue; }
                float current = entry.Source.volume;
                if (entry.LoggedInitial && !entry.LoggedNextFrame && Time.frameCount > entry.InitialFrame)
                {
                    Logger.LogInfo("Thought audio next frame: " + entry.Source.gameObject.name
                        + " id=" + pair.Key + " observed=" + current.ToString("0.###")
                        + " previousApplied=" + entry.AppliedVolume.ToString("0.###"));
                    entry.LoggedNextFrame = true;
                }
                if (entry.HasApplied && Mathf.Abs(current - entry.AppliedVolume) > .01f)
                {
                    if (!entry.Logged)
                    {
                        Logger.LogInfo("Thought audio source rewrote volume: " + entry.Source.gameObject.name
                            + " applied=" + entry.AppliedVolume.ToString("0.###")
                            + " observed=" + current.ToString("0.###"));
                        entry.Logged = true;
                    }
                    entry.BaseVolume = current; // Another controller changed this source during the effect.
                }
                bool moan = entry.Source.gameObject.name == "MainGameVoiceFaceEventBridge.ImpactCue";
                float gain = moan
                    ? _moanAudioGain.Value : _worldAudioGain.Value;
                float next = entry.BaseVolume * Mathf.Lerp(1f, gain, _duckAmount);
                entry.Source.volume = next;
                if (entry.OwnsLowPass && entry.LowPass != null)
                    entry.LowPass.cutoffFrequency = Mathf.Lerp(22000f,
                        moan ? _moanLowPassCutoff.Value : _worldLowPassCutoff.Value, _duckAmount);
                entry.AppliedVolume = next;
                entry.HasApplied = true;
                if (_audioDiagnosticStarted && !entry.LoggedInitial && _audioDiagnosticSources < 24)
                {
                    Logger.LogInfo("Thought audio source: " + entry.Source.gameObject.name
                        + " id=" + pair.Key + " base=" + entry.BaseVolume.ToString("0.###")
                        + " applied=" + next.ToString("0.###")
                        + " playing=" + entry.Source.isPlaying
                        + " lowPass=" + entry.OwnsLowPass);
                    _audioDiagnosticSources++;
                    entry.LoggedInitial = true;
                    entry.InitialFrame = Time.frameCount;
                }
                if (_duckAmount <= .0001f) _deadDuckSources.Add(pair.Key);
            }
            foreach (int id in _deadDuckSources)
            {
                if (_duckSources.TryGetValue(id, out DuckSource entry) && entry.OwnsLowPass && entry.LowPass != null)
                    Destroy(entry.LowPass);
                _duckSources.Remove(id);
            }
        }

        private void RestoreAudioDucking()
        {
            foreach (DuckSource entry in _duckSources.Values)
            {
                if (entry.Source != null && (!entry.HasApplied || Mathf.Abs(entry.Source.volume - entry.AppliedVolume) <= .01f))
                    entry.Source.volume = entry.BaseVolume;
                if (entry.OwnsLowPass && entry.LowPass != null) Destroy(entry.LowPass);
            }
            _duckSources.Clear();
            _duckAmount = 0f;
        }

        private void DrawAudioSettings()
        {
            GUILayout.Label("思考中の音量（思考音声は対象外）");
            Slider("現実音・通常ゲーム音", _worldAudioGain, 0f, 1f);
            Slider("喘ぎ声", _moanAudioGain, 0f, 1f);
            Slider("現実音のこもり (Hz)", _worldLowPassCutoff, 500f, 22000f);
            Slider("喘ぎ声のこもり (Hz)", _moanLowPassCutoff, 500f, 22000f);
            Slider("音量を下げる時間", _audioFadeIn, 0f, 5f);
            Slider("音量を戻す時間", _audioFadeOut, 0f, 5f);
        }
    }
}
