using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private Coroutine _loadRoutine;
        private IEnumerator _loadIterator;
        private Behaviour _originalAnimator, _originalIk;
        private bool _animatorEnabled, _ikEnabled, _vmdOwnsCharacter;

        private void CaptureMainGameControl(ChaControl chara)
        {
            _originalAnimator = chara.objAnim.GetComponent("Animator") as Behaviour;
            _originalIk = chara.objAnim.GetComponent("FullBodyBipedIK") as Behaviour;
            if ((_controller as UnityEngine.Object) != null)
            {
                _originalAnimator = AccessTools.Method(_controller.GetType(), "GetAnimBody")
                    .Invoke(_controller, new object[] { chara }) as Behaviour;
                _originalIk = AccessTools.Method(_controller.GetType(), "GetAnimIK")
                    .Invoke(_controller, new object[] { chara }) as Behaviour;
            }
            _animatorEnabled = _originalAnimator != null && _originalAnimator.enabled;
            _ikEnabled = _originalIk != null && _originalIk.enabled;
        }

        private void ReleaseCharacter(VmdEndReason reason = VmdEndReason.UserStop)
        {
            NeckTrace("release:" + reason);
            if (!_controlTransition.TryBeginRelease()) return;
            bool hadOwnership = _vmdOwnsCharacter || _poseSnapshot != null;
            try {
            if (!_myListSwitching) { StopMyList(); _queuedActorPreset = null; }
            _presetPlaybackRequested = false;
            if (_presetMessage.StartsWith("再生中:") || _presetMessage.StartsWith("準備中")) _presetMessage = "停止しました";
            ControlTransition.Cleanup(ex => LogError("[Release] load " + ex),
                () => { if (_loadRoutine != null) StopCoroutine(_loadRoutine); },
                () => (_loadIterator as IDisposable)?.Dispose());
            _loadRoutine = null;
            _loadIterator = null;
            ControlTransition.Cleanup(ex => LogError("[Release] " + ex), CancelDiagnosticLoad, CancelMusicLoad, ReleaseCameraMotion);
            _optimizedSequences.Clear();
            _motionClockSequence = null; _seekDragging = false; _seekRequested = false;
            if ((_controller as UnityEngine.Object) != null)
            {
                ControlTransition.Cleanup(ex => LogError("[Release] motion " + ex),
                    () => VmdBridgeStop(_controller), () => VmdBridgeDeleteAnim(_controller));
                try { AccessTools.Property(_controller.GetType(), "VMDAnimEnabled").SetValue(_controller, false, null); }
                catch (Exception ex) { LogError("[Release] VMD disable failed: " + ex); }
                // The dependency setter can throw after changing its flag. Restore the
                // actual controls independently so a partial setter cannot retain them.
                try {
                    var face = AccessTools.Field(_controller.GetType(), "faceController")?.GetValue(_controller) as Behaviour;
                    if (face != null) face.enabled = false;
                } catch (Exception ex) { LogError("[Release] face disable failed: " + ex); }
            }
            ControlTransition.Cleanup(ex => LogError("[Release] sound " + ex),
                () => { if (VmdBridgeSoundIsAvailable()) VmdBridgeSoundStop(); });
            ControlTransition.Cleanup(ex => LogError("[Release] restore " + ex), RestoreFaceControl, RestoreBodyCorrection, () => RestorePoseSnapshot(reason));
            _fullBodyOwned = false; _ownedSolver = null; _abmx = null;
            _vmdOwnsCharacter = false;
            _lastIsActive = false;
            _currentAnimTime = 0;
            _animLength = 0;
            SetState(PluginState.CharaReady);
            LogInfo("[Release] character returned to main-game controls");
            } finally {
                // A failing media/dependency cleanup must never retain control of the body.
                try {
                    ControlTransition.Cleanup(ex => LogError("[Release] final restore " + ex),
                        RestoreGameAudio, RestoreDancePartners, RestoreFaceControl, RestoreBodyCorrection, () => RestorePoseSnapshot(reason),
                        () => { if (hadOwnership && _originalAnimator != null) _originalAnimator.enabled = _animatorEnabled; },
                        () => { if (hadOwnership && _originalIk != null) _originalIk.enabled = _ikEnabled; });
                } finally {
                    _fullBodyOwned = false; _ownedSolver = null; _abmx = null;
                    _vmdOwnsCharacter = false; _controlTransition.CompleteRelease();
                }
            }
        }

        private IEnumerator GuardedLoadThenPlay(string path, bool autoPlay)
        {
            // Ensure StartCoroutine assigns its handle before completion/failure can clear it.
            yield return null;
            var workflow = LoadThenPlay(path, autoPlay);
            try {
                while (true) {
                    bool more = false;
                    object current = null;
                    Exception error = null;
                    try { more = workflow.MoveNext(); if (more) current = workflow.Current; }
                    catch (Exception ex) { error = ex; }
                    if (error != null) {
                        _presetMessage = "読込失敗: " + error.GetBaseException().Message;
                        LogError("[Load] " + error);
                        _loadRoutine = null;
                        ReleaseCharacter(VmdEndReason.LoadFailed);
                        yield break;
                    }
                    if (!more) yield break;
                    yield return current;
                }
            } finally { (workflow as IDisposable)?.Dispose(); }
        }

        private IEnumerator LoadThenPlay(string path, bool autoPlay)
        {
            int epoch = _poseEpoch;
            Exception failure = null;
            _loadIterator = PrepareUprightPose(epoch);
            while (failure == null && _loadIterator != null)
            {
                bool more = false;
                object next = null;
                try { more = _loadIterator.MoveNext(); if (more) next = _loadIterator.Current; }
                catch (Exception ex) { failure = ex; }
                if (!more || failure != null) break;
                yield return next;
            }
            (_loadIterator as IDisposable)?.Dispose();
            _loadIterator = null;
            if (epoch != _poseEpoch || !_vmdOwnsCharacter) yield break;
            if (failure == null) try {
                ApplySavedAdjustments(true);
                AccessTools.Method(_controller.GetType(), "LoadVMDAnimation_Internal").Invoke(_controller, new object[] { path, false });
            } catch (Exception ex) { failure = ex; }
            if (failure != null) { _presetMessage = "VMD読込失敗: " + failure.GetBaseException().Message; LogError("[Load] " + failure); ReleaseCharacter(VmdEndReason.LoadFailed); yield break; }
            if (!(bool)AccessTools.Method(_controller.GetType(), "IsVMDLoaded").Invoke(_controller, null)) {
                _presetMessage = "VMD読込失敗: 有効なモーションがありません";
                _loadRoutine = null; ReleaseCharacter(VmdEndReason.LoadFailed); yield break;
            }
            try { WriteMember(_controller, "VMDAnimEnabled", true); }
            catch (Exception ex) { LogError("[Load] enable failed: " + ex); ReleaseCharacter(VmdEndReason.LoadFailed); yield break; }
            try { PrepareSequenceOptimization(); }
            catch (Exception ex) { _optimizedSequences.Clear();
            _motionClockSequence = null; _seekDragging = false; _seekRequested = false; LogWarn("[Performance] using original search: " + ex.Message); }
            if (_presetPlaybackRequested && ((!string.IsNullOrEmpty(_settings.CameraVmdPath) && !System.IO.File.Exists(_settings.CameraVmdPath)) || (!string.IsNullOrEmpty(_settings.LastAudioAbsPath) && !System.IO.File.Exists(_settings.LastAudioAbsPath)))) {
                _presetMessage = "読込失敗: 読込中に音源またはカメラファイルが見つからなくなりました";
                _loadRoutine = null; ReleaseCharacter(VmdEndReason.LoadFailed); yield break;
            }
            if (System.IO.File.Exists(_settings.CameraVmdPath)) LoadCameraMotion(_settings.CameraVmdPath);
            else _cameraSequence?.KillAll();
            ApplySavedAdjustments(false);
            ApplyBodyCorrection();
            VmdBridgeSetAnimPosition(_controller, 0f);
            _settings.LastVmdAbsPath = path;
            SaveSettings("load-vmd");
            if (_settings.AudioAutoLoad && System.IO.File.Exists(_settings.LastAudioAbsPath))
                PlaybackLoadAudio(_settings.LastAudioAbsPath, false);
            while (_musicLoading) { yield return null; if (epoch != _poseEpoch || !_vmdOwnsCharacter) yield break; }
            if (!string.IsNullOrEmpty(_musicError) || (_presetPlaybackRequested && !string.IsNullOrEmpty(_settings.CameraVmdPath) && !string.IsNullOrEmpty(_cameraError))) {
                _presetMessage = "読込失敗: " + (!string.IsNullOrEmpty(_musicError) ? _musicError : _cameraError);
                _loadRoutine = null;
                ReleaseCharacter(VmdEndReason.LoadFailed);
                yield break;
            }
            _loadRoutine = null;
            if (_modelResumeTime >= 0) { VmdBridgeSetAnimPosition(_controller, _modelResumeTime); _cameraSequence?.SetTime(_modelResumeTime); SeekMusic(_modelResumeTime, false); _modelResumeTime = -1; }
            if (autoPlay && !AstralBlocked) PlaybackPlayAll(); else SetState(PluginState.CharaReady);
            if (_presetPlaybackRequested) _presetMessage = "再生中: " + _presetName;
            _presetPlaybackRequested = false;
        }
    }
}
