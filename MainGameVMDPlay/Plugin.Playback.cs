using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    internal enum PluginState
    {
        Idle,
        HSceneEntered,
        CharaReady,
        Playing,
        Paused,
    }

    public sealed partial class Plugin
    {
        private PluginState _state      = PluginState.Idle;
        private object _controller { get => _target.Controller; set => _target.Controller = value; }

        // TickPlayback で使うキャッシュ
        private bool  _lastIsActive   = false;
        private float _currentAnimTime = 0f;
        private float _animLength      = 0f;

        // 毎フレームエラー抑制
        private string _lastTickError     = null;
        private float  _lastTickErrorTime = 0f;

        private void SetState(PluginState next)
        {
            if (_state == next) return;
            LogInfo($"[Playback] state {_state} → {next}");
            _state = next;
        }

        // ── 再生操作 ───────────────────────────────────────────────────────────

        private void PlaybackLoadAndPlay(string absPath, bool autoPlay = true)
        {
            if (AstralBlocked) return;
            if (!_modelReloadStarting) { _modelResumeTime = -1; _modelReloadAt = -1; }
            LogInfo($"[Playback] LoadAndPlay path={absPath} autoPlay={autoPlay}");
            if (!File.Exists(absPath))
            {
                LogError($"[Playback] LoadAndPlay file not found path={absPath}");
                return;
            }
            if ((!string.IsNullOrEmpty(_settings.LastAudioAbsPath) && _settings.AudioAutoLoad && !File.Exists(_settings.LastAudioAbsPath)) ||
                (!string.IsNullOrEmpty(_settings.CameraVmdPath) && !File.Exists(_settings.CameraVmdPath))) {
                _presetMessage = "音源またはカメラVMDが見つかりません"; return;
            }
            if ((IsIndependentTarget && !_actorReady) || _trialChara == null || _trialChara.objBodyBone == null || HasPendingPosture())
            {
                _presetMessage = "キャラ未準備、または体位変更待ちのため再生できません";
                return;
            }

            if (!string.Equals(_selectedVmdForPicker ?? _settings.LastVmdAbsPath, absPath, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_presetName))
                _presetName = Path.GetFileNameWithoutExtension(absPath);
            if (!string.Equals(_selectedVmdForPicker ?? _settings.LastVmdAbsPath, absPath, StringComparison.OrdinalIgnoreCase)) _activePresetPath = null;
            _selectedVmdForPicker = absPath;
            bool presetRequest = _presetPlaybackRequested;
            if (_loadRoutine != null || _vmdOwnsCharacter) ReleaseCharacter();
            _presetPlaybackRequested = presetRequest;
            try {
                CaptureMainGameControl(_trialChara);
                CapturePoseSnapshot();
                _vmdOwnsCharacter = true;
                SuppressGameAudio();
                _barEpoch++;
                CaptureFaceControl(); CaptureBodyCorrection();
                HideDancePartners();
            }
            catch (Exception ex) { LogError("[Load] adjustment setup: " + ex); ReleaseCharacter(); return; }
            _loadRoutine = StartCoroutine(GuardedLoadThenPlay(absPath, autoPlay));
        }

        // ── 音声ロード/再生 ────────────────────────────────────────────────────

        private void PlaybackClearAudio()
        {
            LogInfo("[Playback] ClearAudio");
            if (!VmdBridgeSoundIsAvailable()) return;
            VmdBridgeSoundStop();
            VmdBridgeSoundClearClip();
            _settings.LastAudioAbsPath = "";
            SaveSettings("clear-audio");
        }

        private void PlaybackPlayAll()
        {
            NeckTrace("play-request");
            if (AstralBlocked) return;
            if (!_vmdOwnsCharacter && _trialChara != null) {
                PlaybackLoadAndPlay(_settings.LastVmdAbsPath, true);
                return;
            }
            if (_controller == null) { LogWarn("[Playback] no controller, cannot play-all"); return; }
            LogInfo("[Playback] play-all: VMD+audio");
            ResumeFaceControl();
            if (!VmdBridgePlay(_controller)) { _presetMessage = "VMD再生開始に失敗しました"; ReleaseCharacter(); return; }
            PlayCameraMotion();
            if (VmdBridgeSoundIsAvailable() && !string.IsNullOrEmpty(VmdBridgeSoundGetAudioFilePath()))
                VmdBridgeSoundPlay();
            _lastIsActive = true;
            SetState(PluginState.Playing);
        }

        private void PlaybackPlay()
        {
            if (AstralBlocked) return;
            if (!_vmdOwnsCharacter && _controller != null) {
                PlaybackLoadAndPlay(_settings.LastVmdAbsPath, true);
                return;
            }
            if (_controller == null) { LogWarn("[Playback] no controller, cannot play"); return; }
            LogInfo("[Playback] Play (sync VMD+audio)");
            ResumeFaceControl();
            bool ok = VmdBridgePlay(_controller);
            if (ok) PlayCameraMotion();
            if (VmdBridgeSoundIsAvailable() && !string.IsNullOrEmpty(VmdBridgeSoundGetAudioFilePath()))
                VmdBridgeSoundPlay();
            if (ok) { _lastIsActive = true; SetState(PluginState.Playing); }
        }

        private void PlaybackPause()
        {
            if (_controller == null) { LogWarn("[Playback] no controller, cannot pause"); return; }
            LogInfo("[Playback] Pause (sync VMD+audio)");
            bool ok = VmdBridgePause(_controller);
            _cameraSequence?.PauseSequences();
            if (VmdBridgeSoundIsAvailable()) VmdBridgeSoundPause();
            if (ok) SetState(PluginState.Paused);
        }

        private void PlaybackStop()
        {
            ReleaseCharacter();
        }

        private void PlaybackRestart()
        {
            if (AstralBlocked) return;
            if (!_vmdOwnsCharacter && _controller != null) {
                PlaybackLoadAndPlay(_settings.LastVmdAbsPath, true);
                return;
            }
            if (_controller == null) { LogWarn("[Playback] no controller, cannot restart"); return; }
            LogInfo("[Playback] Restart (sync VMD+audio)");
            ResumeFaceControl();
            bool ok = VmdBridgeRestart(_controller);
            if (ok) PlayCameraMotion();
            if (VmdBridgeSoundIsAvailable() && !string.IsNullOrEmpty(VmdBridgeSoundGetAudioFilePath()))
            {
                VmdBridgeSoundStop();
                VmdBridgeSoundPlay();
            }
            if (ok) { _lastIsActive = true; SetState(PluginState.Playing); }
        }

        private void PlaybackSeekTo(float t)
        {
            if (_controller == null || !_vmdOwnsCharacter || _loadRoutine != null) return;
            VmdBridgeSetAnimPosition(_controller, t);
            _cameraSequence?.SetTime(t);
            SeekMusic(t, _state == PluginState.Playing);
            _motionClockSequence?.Apply(t);
            _cameraSequence?.Apply(t);
            _currentAnimTime = t;
            // No per-drag logging or disk work.
        }

        private void PlaybackUnloadAll()
        {
            LogInfo("[Playback] UnloadAll begin");
            ReleaseCharacter(VmdEndReason.SceneExit);
            if (VmdBridgeSoundIsAvailable())
            {
                LogInfo("[Playback] stop audio");
                VmdBridgeSoundStop();
                VmdBridgeSoundClearClip();
            }
            if (_controller != null)
            {
                LogInfo("[Playback] stop");
                VmdBridgeStop(_controller);
                LogInfo("[Playback] deleteAnim");
                VmdBridgeDeleteAnim(_controller);
                RetireController();
            }
            _lastIsActive    = false;
            _currentAnimTime = 0f;
            _animLength      = 0f;
            SetState(PluginState.Idle);
            LogInfo("[Playback] UnloadAll done");
        }

        // ── 毎フレーム監視 ─────────────────────────────────────────────────────

        private void TickPlayback()
        {
            if (_seekRequested) { _seekRequested = false; PlaybackSeekTo(_seekDragValue); }
            if (_seekDragging && !Input.GetMouseButton(0)) _seekDragging = false;
            if (_controller == null)
            {
                // コントローラが外部で破棄された場合
                if (_state != PluginState.Idle && _state != PluginState.HSceneEntered)
                {
                    LogWarn("[Playback] controller lost, resetting to Idle");
                    SetState(PluginState.Idle);
                }
                return;
            }

            try
            {
                if (!_seekDragging && _state == PluginState.Playing && _mediaEnded) { FinishMyListEntry(); return; }
                bool  active = VmdBridgeIsActive(_controller);
                float time   = VmdBridgeGetAnimTime(_controller);
                _animLength  = VmdBridgeGetAnimationLength();

                if (active != _lastIsActive)
                {
                    LogInfo($"[Playback] active changed {_lastIsActive}→{active}");
                    _lastIsActive = active;

                    if (!_seekDragging && !active && _state == PluginState.Playing)
                    {
                        LogInfo($"[Playback] animation ended at t={_currentAnimTime:F2}");
                        FinishMyListEntry();
                        return;
                    }
                }
                _currentAnimTime = time;
                _lastTickError   = null;
            }
            catch (Exception ex)
            {
                string msg = $"{ex.GetType().Name}: {ex.Message}";
                float now = Time.unscaledTime;
                if (msg != _lastTickError || now - _lastTickErrorTime > 5f)
                {
                    LogError($"[Playback] TickPlayback ex={msg}");
                    _lastTickError     = msg;
                    _lastTickErrorTime = now;
                }
            }
        }

        // ── Hシーン開始時のキャラ待機コルーチン ───────────────────────────────

        private IEnumerator WaitForCharaReady()
        {
            var scene = _hSceneProc; int targetGeneration = _target.Generation;
            var lstFemaleField = AccessTools.Field(typeof(HSceneProc), "lstFemale");

            bool lastLoadEnd = false;
            bool lastObjBody = false;
            int frame = 0;
            const int TimeoutFrames = 600; // 約10秒

            while (frame < TimeoutFrames && _hSceneProc != null && _cfgEnabled.Value)
            {
                if (scene != _hSceneProc) yield break;
                frame++;

                List<ChaControl> lst = null;
                try { lst = lstFemaleField?.GetValue(_hSceneProc) as List<ChaControl>; } catch { }

                if (lst == null || lst.Count == 0) { yield return null; continue; }

                ChaControl chara = lst[0];
                if (chara == null) { yield return null; continue; }

                bool loadEnd = chara.loadEnd;
                bool hasBody = chara.objBody != null;

                if (loadEnd != lastLoadEnd || hasBody != lastObjBody)
                {
                    LogDebug($"[Playback] wait frame={frame} loadEnd={loadEnd} objBody={hasBody}");
                    lastLoadEnd = loadEnd;
                    lastObjBody = hasBody;
                }

                if (!loadEnd || !hasBody) { yield return null; continue; }

                string charaName = chara.chaFile?.parameter?.fullname ?? "?";
                LogInfo($"[Playback] chara ready name={charaName} frames={frame}");

                if (scene != _hSceneProc) yield break;
                _hActor = chara;
                if (IsIndependentTarget || targetGeneration != _target.Generation) yield break;
                _target.Actor = chara; _target.Scene = scene;
                TryInstallController(chara);
                yield break;
            }

            if (scene != _hSceneProc || IsIndependentTarget || targetGeneration != _target.Generation) yield break;
            LogError($"[Playback] WaitForCharaReady timed out frames={frame} hscene={_hSceneProc != null}");
            SetState(PluginState.Idle);
        }

        private void TryInstallController(ChaControl chara)
        {
            // Resolve original skeleton before VMD creates same-named dummy bones.
            // Studio likewise initializes ExpressionBone before installing motion tools.
            if (chara.expression == null) chara.InitializeExpression(false);
            CaptureMainGameControl(chara);
            PrepareDiagnosticTrial(chara);
            _controller = VmdBridgeInstallController(chara);
            if (_controller == null)
            {
                LogError("[Playback] Controller.Install returned null — cannot use VMD in this HScene");
                SetState(PluginState.Idle);
                return;
            }

            SetState(PluginState.CharaReady);

            if (_settings.AutoLoadLast && !string.IsNullOrEmpty(_settings.LastVmdAbsPath))
            {
                LogInfo($"[Playback] AutoLoadLast path={_settings.LastVmdAbsPath}");
                PlaybackLoadAndPlay(_settings.LastVmdAbsPath, _settings.AutoPlayOnLoad);
            }
        }
    }
}
