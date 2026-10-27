using System;
using System.Collections.Generic;
using System.IO;
using MainGameVoiceFaceEventBridge.Audio;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class ExternalVoicePlayer
    {
        [Serializable]
        private sealed class CueSelection { public string[] moan; public string[] kiss; public string[] sbv2; public string[] deep; }
        private sealed class CueAsset { internal PcmData Pcm; internal AudioClip Clip; }
        private readonly List<CueAsset> _cueAssets = new List<CueAsset>();
        private readonly HashSet<string> _cueOwners = new HashSet<string>(StringComparer.Ordinal);
        private readonly ImpactCueState _cueState = new ImpactCueState();
        private readonly System.Random _cueTriggerRandom = new System.Random();
        private ImpactCueSettings _cueSettings;
        private CueShuffleBag _originalCueBag, _sbv2CueBag, _deepCueBag;
        private int _originalCueCount, _sbv2CueCount, _deepCueCount;
        private bool _priorityDeepCueActive;
        private bool _priorityPistonCueActive;
        private GameObject _cueHost;
        private AudioSource _cueSource;
        private SampleFadeFilter _dialogueFade, _cueFade;
        private AudioClip _selectedCue;
        private ChaControl _cueFemale;
        private string _activeCueOwner;
        private int _cachedFadeInMs = -1, _cachedFadeOutMs = -1;
        private bool _cueReady;
        private Func<bool> _gameVoiceBusyProbe;
        internal bool ImpactCueBusy => _cueState.HoldsDialogue;
        internal bool ImpactCueActive => _cueState.Busy;
        internal bool PriorityDeepCueActive => _priorityDeepCueActive && _cueState.Busy;
        internal bool PriorityAibuCueActive => (_priorityDeepCueActive || _priorityPistonCueActive) && _cueState.Busy;
        internal bool DeepCueReady => _cueReady && _deepCueBag != null;
        internal void SetGameVoiceBusyProbe(Func<bool> probe) => _gameVoiceBusyProbe = probe;
        private bool GameVoiceBusy => _gameVoiceBusyProbe != null && _gameVoiceBusyProbe();

        internal void InitializeImpactCues(string directory, ImpactCueSettings settings)
        {
            _cueSettings = settings;
            EnsureSource();
            _dialogueFade = _host.AddComponent<SampleFadeFilter>();
            _cueHost = new GameObject("MainGameVoiceFaceEventBridge.ImpactCue");
            UnityEngine.Object.DontDestroyOnLoad(_cueHost);
            _cueSource = _cueHost.AddComponent<AudioSource>();
            _cueSource.playOnAwake = false;
            _cueSource.loop = false;
            _cueSource.spatialBlend = 0;
            _cueFade = _cueHost.AddComponent<SampleFadeFilter>();
            try
            {
                CueSelection selection = JsonUtility.FromJson<CueSelection>(File.ReadAllText(Path.Combine(directory, "selection.json")));
                LoadCueGroup(directory, "Moan", selection.moan);
                LoadCueGroup(directory, "Kiss", selection.kiss);
                _originalCueCount = _cueAssets.Count;
                if (_originalCueCount != 22) throw new InvalidDataException("Expected 22 original cues, found " + _originalCueCount);
                _originalCueBag = new CueShuffleBag(_originalCueCount, new System.Random());
                if (selection.sbv2 != null && selection.sbv2.Length > 0)
                {
                    LoadCueGroup(directory, "SBV2", selection.sbv2);
                    _sbv2CueCount = _cueAssets.Count - _originalCueCount;
                    _sbv2CueBag = new CueShuffleBag(_sbv2CueCount, new System.Random());
                }
                if (selection.deep != null && selection.deep.Length > 0)
                {
                    LoadCueGroup(directory, "Deep", selection.deep);
                    _deepCueCount = _cueAssets.Count - _originalCueCount - _sbv2CueCount;
                    _deepCueBag = new CueShuffleBag(_deepCueCount, new System.Random());
                }
                LoadPistonCues(Path.Combine(directory, "Piston"));
                LoadPistonCues(Path.Combine(directory, "HardPiston"), true);
                LoadPistonCues(Path.Combine(directory, "Ogazm"), false, true);
                RebuildCueFades();
                _cueReady = true;
                _logInfo?.Invoke("[impact-cue] preloaded=" + _cueAssets.Count + " reusable PCM clips; Original=" + _originalCueCount + " SBV2=" + _sbv2CueCount + " selected=" + _cueSettings.CueSource.Value);
            }
            catch (Exception ex) { Plugin.LogImpactCueDiagnostic("preload failed: " + ex); _logError?.Invoke("[impact-cue] preload failed: " + ex.Message); }
            Plugin.LogImpactCueDiagnostic("preload ready=" + _cueReady + " Original=" + _originalCueCount + " SBV2=" + _sbv2CueCount);
        }

        private void LoadCueGroup(string directory, string group, string[] names)
        {
            if (names == null) throw new InvalidDataException("Missing cue selection: " + group);
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name)
                    throw new InvalidDataException("Invalid cue filename");
                string path = Path.Combine(directory, group, name);
                if (!WavFileLoader.TryLoadPcm(path, out PcmData pcm, out string error))
                    throw new InvalidDataException(name + ": " + error);
                if ((double)pcm.Pcm.Length / pcm.Channels / pcm.SampleRate > 4.001)
                    throw new InvalidDataException(name + ": longer than 4 seconds");
                _cueAssets.Add(new CueAsset { Pcm = pcm });
            }
        }

        private void RebuildCueFades()
        {
            int fadeIn = _cueSettings.CueFadeInMs.Value, fadeOut = _cueSettings.CueFadeOutMs.Value;
            if (_cachedFadeInMs == fadeIn && _cachedFadeOutMs == fadeOut) return;
            foreach (CueAsset asset in _cueAssets)
            {
                PcmData pcm = asset.Pcm;
                float[] samples = SampleGainEnvelope.FadeClip(pcm.Pcm, pcm.Channels, pcm.SampleRate, fadeIn, fadeOut);
                if (asset.Clip == null)
                    asset.Clip = AudioClip.Create(Path.GetFileNameWithoutExtension(pcm.Path),
                        pcm.Pcm.Length / pcm.Channels, pcm.Channels, pcm.SampleRate, false);
                if (!asset.Clip.SetData(samples, 0)) throw new InvalidOperationException("Cue SetData failed: " + pcm.Path);
            }
            _cachedFadeInMs = fadeIn;
            _cachedFadeOutMs = fadeOut;
        }

        internal void SetImpactCueEnabled(string owner, bool enabled)
        {
            if (string.IsNullOrEmpty(owner)) return;
            if (enabled) _cueOwners.Add(owner);
            else
            {
                _cueOwners.Remove(owner);
                if (_activeCueOwner == owner) ApplyCueActions(_cueState.Disable());
            }
        }

        internal bool RequestImpactCue(string owner, ChaControl female)
        {
            if (!_cueReady || !_cueOwners.Contains(owner) || !CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom)
            {
                Plugin.LogImpactCueDiagnostic("rejected owner=" + owner + " ready=" + _cueReady + " ownerEnabled=" + _cueOwners.Contains(owner) + " blankRoom=" + CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom);
                return false;
            }
            float now = Time.unscaledTime;
            if (_cueState.Busy || now < _cueState.NextAllowedAt)
            {
                Plugin.LogImpactCueDiagnostic("rejected owner=" + owner + " busy=" + _cueState.Busy + " phase=" + _cueState.Phase + " remainingCooldown=" + Math.Max(0, _cueState.NextAllowedAt - now).ToString("F2"));
                return false;
            }
            if (!_currentThought && GameVoiceBusy) { Plugin.LogImpactCueDiagnostic("rejected owner=" + owner + " nativeVoiceBusy=true"); return false; }
            bool useSbv2 = _cueSettings.CueSource.Value == "SBV2";
            CueShuffleBag bag = useSbv2 ? _sbv2CueBag : _originalCueBag;
            if (bag == null) { Plugin.LogImpactCueDiagnostic("rejected owner=" + owner + " source=" + _cueSettings.CueSource.Value + " bagMissing=true"); return false; }
            double roll = _cueTriggerRandom.NextDouble() * 100;
            if (roll >= _cueSettings.TriggerChancePercent.Value)
            {
                Plugin.LogImpactCueDiagnostic("rejected owner=" + owner + " chanceRoll=" + roll.ToString("F1") + " threshold=" + _cueSettings.TriggerChancePercent.Value);
                return false;
            }
            bool speaking = !_currentThought && _source != null && (_source.isPlaying || _silencePhase == SilencePhase.Paused);
            if (!_cueState.TryBegin(now, speaking, out CueAction action)) return false;
            _activeCueOwner = owner;
            _cueFemale = speaking && _dialogueFemale != null ? _dialogueFemale : female;
            _selectedCue = _cueAssets[(useSbv2 ? _originalCueCount : 0) + bag.Next()].Clip;
            // Transfer ownership from the legacy boost/silence envelope before pausing.
            if (speaking) _dialogueFade.Envelope.Reset(GetCurrentOutputGain());
            _boostActive = false;
            if (_silencePhase == SilencePhase.Paused) _source.UnPause();
            ResetSilenceState();
            if (_boostFilter != null) _boostFilter.Gain = 1;
            ApplyCueActions(action);
            if (_selectedCue == null) { Plugin.LogImpactCueDiagnostic("rejected owner=" + owner + " selectedClip=null"); return false; }
            Plugin.LogImpactCueDiagnostic("accepted owner=" + owner + " dialogue=" + speaking + " clip=" + _selectedCue.name);
            _logInfo?.Invoke("[impact-cue] accepted owner=" + owner + " dialogue=" + speaking + " clip=" + _selectedCue.name);
            return true;
        }

        internal bool RequestPriorityDeepCue(string owner, ChaControl female)
        {
            if (!_cueReady || _deepCueBag == null || !_cueOwners.Contains(owner)) return false;
            // A new deep hit replaces every external line and cue, including an earlier deep hit.
            AbortImpactCue();
            Stop("priority deep hit");
            if (!_cueState.TryBegin(Time.unscaledTime, false, out CueAction action)) return false;
            _priorityDeepCueActive = true;
            _activeCueOwner = owner;
            _cueFemale = female;
            _selectedCue = _cueAssets[_originalCueCount + _sbv2CueCount + _deepCueBag.Next()].Clip;
            ApplyCueActions(action);
            _logInfo?.Invoke("[impact-cue] priority deep clip=" + _selectedCue?.name);
            return _selectedCue != null && _cueSource.isPlaying;
        }


        internal void CancelImpactCueForMotion()
        {
            // Keep checkbox/owner settings intact; eligibility returns when the motion returns to S/W.
            ApplyCueActions(_cueState.Disable());
        }

        private void TickImpactCue()
        {
            if (_cueSettings == null) return;
            if (!_cueState.Busy)
            {
                if (_cueReady) RebuildCueFades(); // cached PCM only; never read files on a trigger
                return;
            }
            ApplyCueActions(_cueState.Tick(Time.unscaledTime, _source != null && _source.isPlaying,
                _dialogueFade.Envelope.Complete, _cueSource.isPlaying, _cueFade.Envelope.Complete,
                _cueSettings.CooldownMs.Value, _cueSettings.DialogueDowntimeMultiplier.Value));
            if (_cueSource.isPlaying) BindCueLipSync();
        }

        private void ApplyCueActions(CueAction action)
        {
            if (action == CueAction.None || _cueSettings == null) return;
            // Recheck immediately before Play; never overlap a pre-existing native load.
            if ((action & CueAction.StartCue) != 0 && !_currentThought && GameVoiceBusy)
            {
                Plugin.LogImpactCueDiagnostic("start aborted nativeVoiceBusy=true");
                _cueState.Abort();
                ApplyCueActions(CueAction.FinishCue | CueAction.ResumeDialogue);
                return;
            }
            if ((action & CueAction.FadeDialogue) != 0)
                _dialogueFade.FadeTo(0, _cueSettings.DialogueFadeOutMs.Value);
            if ((action & CueAction.PauseDialogue) != 0)
            {
                _source.Pause();
                _logInfo?.Invoke("[impact-cue] dialogue paused sample=" + _source.timeSamples);
            }
            if ((action & CueAction.StartCue) != 0)
            {
                _cueSource.clip = _selectedCue;
                _cueSource.volume = _priorityPistonCueActive ? _pistonCueVolume : _cueSettings.Volume.Value;
                _cueSource.pitch = 1;
                _cueFade.Envelope.Reset(1);
                _cueEmitter?.Bind(_cueFemale);
                _cueSource.Play();
                Plugin.LogImpactCueDiagnostic("started clip=" + (_selectedCue != null ? _selectedCue.name : "<none>") + " isPlaying=" + _cueSource.isPlaying + " volume=" + _cueSource.volume);
                _cueState.RecordCueStarted(Time.unscaledTime, _selectedCue.length);
                BindCueLipSync();
            }
            if ((action & CueAction.FadeCue) != 0)
                _cueFade.FadeTo(0, _cueSettings.StopFadeOutMs.Value);
            if ((action & CueAction.FinishCue) != 0)
            {
                _cueSource.Stop();
                _cueSource.clip = null;
                _cueEmitter?.Clear();
                _selectedCue = null;
                _activeCueOwner = null;
                _priorityDeepCueActive = false;
                _priorityPistonCueActive = false;
                _hardPistonCueActive = false;
                _ogazmCueActive = false;
                if (_currentClip != null && _source.isPlaying) TryBindLipSync(_dialogueFemale);
                else if ((action & CueAction.ResumeDialogue) == 0) TryClearLipSyncBinding();
                _cueFemale = null;
                _logInfo?.Invoke("[impact-cue] finished");
            }
            if ((action & CueAction.ResumeDialogue) != 0 && _currentClip != null)
            {
                // UnPause preserves the stream position. Never Play() or assign time/timeSamples here.
                _dialogueFade.FadeTo(1, _cueSettings.DialogueFadeInMs.Value);
                _dialogueEmitter?.Bind(_dialogueFemale);
                _source.UnPause();
                TryBindLipSync(_dialogueFemale);
                _logInfo?.Invoke("[impact-cue] dialogue resumed sample=" + _source.timeSamples);
            }
        }

        private void BindCueLipSync()
        {
            if (_cueFemale == null) return;
            try
            {
                if (_lipSyncBoundFemale != null && _lipSyncBoundFemale != _cueFemale) TryClearLipSyncBinding();
                _cueFemale.SetLipSync(_cueSource);
                _lipSyncBoundFemale = _cueFemale;
                TryDisableMouthFixedDuringExternalPlay(_cueFemale);
            }
            catch (Exception ex) { _logWarn?.Invoke("[impact-cue] lip sync: " + ex.Message); }
        }

        private void AbortImpactCue()
        {
            _priorityDeepCueActive = false;
            _priorityPistonCueActive = false;
            _hardPistonCueActive = false;
            _ogazmCueActive = false;
            _cueState.Abort();
            if (_cueSource != null) { _cueSource.Stop(); _cueSource.clip = null; }
            _cueEmitter?.Clear();
            _selectedCue = null;
            _activeCueOwner = null;
            _cueFemale = null;
            if (_dialogueFade != null) _dialogueFade.Envelope.Reset(1);
        }

        private void DisposeImpactCues()
        {
            AbortImpactCue();
            if (_cueHost != null) UnityEngine.Object.Destroy(_cueHost);
            foreach (CueAsset asset in _cueAssets)
                if (asset.Clip != null) UnityEngine.Object.Destroy(asset.Clip);
            _cueAssets.Clear();
            _cueOwners.Clear();
        }
    }
}
