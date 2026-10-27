using System.Collections.Generic;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private float _motionGaugeCm;
        private ChaControl _gaugeFaceFemale;
        private int _gaugeFaceOriginalEyes, _gaugeFaceOriginalMouth;
        private int _gaugeFaceTonguePattern;
        private float _nextGaugeMouthChangeAt;
        private int ChooseGaugeMouthPattern(int previous)
        {
            var choices = new List<int>();
            if (_settings.MouthPattern24Enabled) choices.Add(24);
            if (_settings.MouthPattern43Enabled) choices.Add(43);
            if (_settings.MouthPattern44Enabled) choices.Add(44);
            if (_settings.MouthPattern45Enabled) choices.Add(45);
            if (choices.Count == 0) return 24;
            if (choices.Count > 1) choices.Remove(previous);
            return choices[Random.Range(0, choices.Count)];
        }

        private void UpdateGaugeFace()
        {
            bool active = _settings != null && _settings.Enabled && _settings.MotionGaugeEnabled &&
                _model != null && _female != null && _female.fileStatus != null &&
                _scene != null && _scene.flags != null && _scene.flags.mode == HFlag.EMode.aibu &&
                _selectedTarget == TargetKind.Vaginal &&
                _motionGaugeCm >= _settings.MotionGaugeFullCm * _settings.FaceStartGaugeFraction;
            if (!active)
            {
                ReleaseGaugeFace();
                return;
            }
            if (_gaugeFaceFemale != _female)
            {
                ReleaseGaugeFace();
                _gaugeFaceFemale = _female;
                _gaugeFaceOriginalEyes = _female.fileStatus.eyesPtn;
                _gaugeFaceOriginalMouth = _female.fileStatus.mouthPtn;
                _gaugeFaceTonguePattern = ChooseGaugeMouthPattern(-1);
                _nextGaugeMouthChangeAt = Time.unscaledTime + _settings.MouthChangeSeconds;
            }
            if (Time.unscaledTime >= _nextGaugeMouthChangeAt)
            {
                _gaugeFaceTonguePattern = ChooseGaugeMouthPattern(_gaugeFaceTonguePattern);
                _nextGaugeMouthChangeAt = Time.unscaledTime + _settings.MouthChangeSeconds;
            }
            if (_female.fileStatus.eyesPtn != _settings.HeartEyesPattern)
                _female.ChangeEyesPtn(_settings.HeartEyesPattern, true);
            if (_female.fileStatus.mouthPtn != _gaugeFaceTonguePattern)
                _female.ChangeMouthPtn(_gaugeFaceTonguePattern, true);
        }

        private void ReleaseGaugeFace()
        {
            if (_gaugeFaceFemale != null && _gaugeFaceFemale.fileStatus != null)
            {
                if (_settings != null && _gaugeFaceFemale.fileStatus.eyesPtn == _settings.HeartEyesPattern)
                    _gaugeFaceFemale.ChangeEyesPtn(_gaugeFaceOriginalEyes, true);
                if (_gaugeFaceFemale.fileStatus.mouthPtn == _gaugeFaceTonguePattern)
                    _gaugeFaceFemale.ChangeMouthPtn(_gaugeFaceOriginalMouth, true);
            }
            _gaugeFaceFemale = null;
        }
        private float _slowSeconds;
        private int _strokeDirection;
        private float _strokeCm;
        private float _strokeStartTime;
        private bool _deepFiredThisStroke;
        private float _deepPulse;
        private bool _deepReturning;
        private float _fastPulse;
        private float _lastReversalTime = -10f;
        private int _recentReversals;
        private float _deepHoldUntil;
        private Vector3 _lastRigidTip;
        private bool _hasRigidTip;
        private Vector3 _motionPushDirection;
        private float _motionBodyOffset;
        private float _motionUpperOffset;
        private float _motionSlowActive;
        private float _bodyEnableWeight;
        private float _nextPistonVoiceAttemptAt;
        private float _nextHardPistonVoiceAttemptAt;
        private readonly Queue<MotionDistanceSample> _hardDistanceSamples = new Queue<MotionDistanceSample>();
        private readonly Queue<float> _hardReversalTimes = new Queue<float>();
        private float _hardWindowDistanceCm;
        private bool _hardPistonActive;
        private float _hardLastQualifiedAt;
        private bool _ogazmCueRequestedThisEntry;
        private bool _pistonVoicePending, _hardVoicePending, _ogazmVoicePending;
        private bool _pistonPlayedThisEntry, _hardPlayedThisEntry;
        private struct MotionDistanceSample { internal float At; internal float Cm; }

        // Shared primitive-only API: HipHijack and Composer bind it once through reflection.
        public static bool TryGetMotionState(out Vector3 pushDirection, out float bodyOffset,
            out float upperOffset, out float slowActive, out float gauge01)
        {
            Plugin p = Instance;
            bool ready = p != null && p._settings != null;
            pushDirection = p != null ? p._motionPushDirection : Vector3.zero;
            bodyOffset = ready ? p._motionBodyOffset * p._bodyEnableWeight : 0f;
            upperOffset = ready ? p._motionUpperOffset * p._bodyEnableWeight : 0f;
            slowActive = p != null ? p._motionSlowActive : 0f;
            gauge01 = ready ? Mathf.Clamp01(p._motionGaugeCm / p._settings.MotionGaugeFullCm) : 0f;
            return ready && p._motionActive && p._scene != null &&
                   p._scene.flags != null && p._scene.flags.mode == HFlag.EMode.aibu;
        }

        // Entry -> deep is the stable insertion axis. The body push direction above
        // may tilt with the hand movement and must not steer Composer's orbit plane.
        public static bool TryGetInsertionAxis(out Vector3 axis)
        {
            axis = Vector3.zero;
            Plugin p = Instance;
            if (p == null || p._settings == null || !p._settings.Enabled ||
                p._model == null || p._selectedTarget == TargetKind.None ||
                p._scene == null || p._scene.flags == null ||
                p._scene.flags.mode != HFlag.EMode.aibu) return false;
            if (p._selectedAxis.sqrMagnitude < 0.5f) return false;
            axis = p._selectedAxis;
            return true;
        }

        private void ResetVibeReaction()
        {
            if (MotionTraceEnabled && (_motionActive || _motionGaugeCm > 0f))
                TraceVibeEvent("RESET", "gaugeCm=" + TraceNumber(_motionGaugeCm)
                    + " bodyM=" + TraceNumber(_motionBodyOffset));
            _motionActive = false;
            _motionGaugeCm = 0f;
            _slowSeconds = 0f;
            _strokeDirection = 0;
            _strokeCm = 0f;
            _strokeStartTime = 0f;
            _deepFiredThisStroke = false;
            _deepPulse = 0f;
            _deepReturning = false;
            _fastPulse = 0f;
            _recentReversals = 0;
            _lastReversalTime = -10f;
            _deepHoldUntil = 0f;
            _hasRigidTip = false;
            _motionBodyOffset = 0f;
            _motionUpperOffset = 0f;
            _motionSlowActive = 0f;
            _bodyEnableWeight = 0f;
            _motionPushDirection = Vector3.zero;
            _nextPistonVoiceAttemptAt = 0f;
            _ogazmCueRequestedThisEntry = false;
            _pistonVoicePending = _hardVoicePending = _ogazmVoicePending = false;
            _pistonPlayedThisEntry = _hardPlayedThisEntry = false;
            ResetHardPistonMotion();
        }

        private void ResetHardPistonMotion()
        {
            _hardDistanceSamples.Clear();
            _hardReversalTimes.Clear();
            _hardWindowDistanceCm = 0f;
            _hardPistonActive = false;
            _hardLastQualifiedAt = 0f;
            _nextHardPistonVoiceAttemptAt = 0f;
        }

        private void PauseAibuMotion(string reason = "unspecified")
        {
            TracePause(reason);
            _aibuHasSample = false;
            _aibuMotion.Reset();
            _aibuStrongVoice = false;
            _voiceHookObserved = false;
            _strokeDirection = 0;
            _strokeCm = 0f;
            _recentReversals = 0;
            _deepFiredThisStroke = false;
            _deepHoldUntil = 0f;
            _slowSeconds = 0f;
            _motionSlowActive = 0f;
            float dt = Mathf.Max(0f, Time.deltaTime);
            _deepPulse = Mathf.MoveTowards(_deepPulse, 0f,
                dt / _settings.DeepReturnSeconds);
            _fastPulse = 0f;
            _hasRigidTip = false;
            ResetHardPistonMotion();
            _bodyEnableWeight = Mathf.MoveTowards(_bodyEnableWeight,
                _settings.BodyReactionEnabled ? 1f : 0f, dt * 3f);
            _motionGaugeCm = Mathf.MoveTowards(_motionGaugeCm, 0f,
                _settings.MotionGaugeDecayCmPerSecond * dt);
            UpdateOgazmCueTrigger(false);
            float returnSpeed = _deepReturning
                ? _settings.DeepReactionMetres / _settings.DeepReturnSeconds : _settings.BodyReturnSpeed;
            _motionBodyOffset = Mathf.MoveTowards(_motionBodyOffset, 0f, dt * returnSpeed);
            if (_deepPulse <= 0f && Mathf.Abs(_motionBodyOffset) < 0.0001f)
                _deepReturning = false;
            _motionUpperOffset = Mathf.MoveTowards(_motionUpperOffset, 0f,
                dt * _settings.UpperBodyReturnSpeed);
            _motionActive = Mathf.Abs(_motionBodyOffset) > 0.0001f || _motionGaugeCm > 0.0001f;
        }

        private void UpdateVibeReaction(Vector3 axis, float pathLength, float depth,
            float signedTravel, Vector3 rigidTip)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _bodyEnableWeight = Mathf.MoveTowards(_bodyEnableWeight,
                _settings.BodyReactionEnabled ? 1f : 0f, dt * 3f);
            _motionActive = true;
            _motionGaugeCm = _settings.MotionGaugeEnabled
                ? Mathf.Clamp(_motionGaugeCm + Mathf.Abs(signedTravel) * 100f -
                    _settings.MotionGaugeDecayCmPerSecond * dt, 0f, _settings.MotionGaugeFullCm)
                : 0f;
            float gauge = Mathf.Clamp01(_motionGaugeCm / _settings.MotionGaugeFullCm);
            UpdateOgazmCueTrigger(true);
            float speed = Mathf.Abs(signedTravel) / dt;
            float now = Time.unscaledTime;
            float travelledCm = Mathf.Abs(signedTravel) * 100f;
            _hardDistanceSamples.Enqueue(new MotionDistanceSample { At = now, Cm = travelledCm });
            _hardWindowDistanceCm += travelledCm;
            while (_hardDistanceSamples.Count > 0 &&
                   now - _hardDistanceSamples.Peek().At > _settings.HardPistonWindowSeconds)
                _hardWindowDistanceCm -= _hardDistanceSamples.Dequeue().Cm;
            while (_hardReversalTimes.Count > 0 &&
                   now - _hardReversalTimes.Peek() > _settings.HardPistonWindowSeconds)
                _hardReversalTimes.Dequeue();
            int direction = signedTravel > 0f ? 1 : signedTravel < 0f ? -1 : 0;

            if (direction != 0)
            {
                if (_strokeDirection != 0 && direction != _strokeDirection)
                {
                    float strokeSeconds = Mathf.Max(0.001f, Time.time - _strokeStartTime);
                    bool rapidStroke = _strokeCm >= _settings.PistonMinStrokeCm &&
                        strokeSeconds <= _settings.PistonMaxStrokeSeconds &&
                        _strokeCm * 0.01f / strokeSeconds >= _settings.FastMinSpeed;
                    float strokeSpeed = _strokeCm * 0.01f / strokeSeconds;
                    float reversalGap = Time.time - _lastReversalTime;
                    _recentReversals = rapidStroke
                        ? (reversalGap <= _settings.PistonReversalWindowSeconds ? _recentReversals + 1 : 1) : 0;
                    if (rapidStroke) _hardReversalTimes.Enqueue(now);
                    LogMotionEvent("REVERSAL", "strokeCm=" + _strokeCm.ToString("F1") +
                        " strokeSec=" + strokeSeconds.ToString("F3") +
                        " avgMps=" + strokeSpeed.ToString("F3") +
                        " instantMps=" + speed.ToString("F3") +
                        " gapSec=" + reversalGap.ToString("F3") +
                        " rapid=" + rapidStroke + " consecutive=" + _recentReversals +
                        " thresholdMps=" + _settings.FastMinSpeed.ToString("F3"));
                    _lastReversalTime = Time.time;
                    _strokeCm = 0f;
                    _strokeStartTime = Time.time;
                    _deepFiredThisStroke = false;
                }
                else if (_strokeDirection == 0) _strokeStartTime = Time.time;
                _strokeDirection = direction;
                _strokeCm += Mathf.Abs(signedTravel) * 100f;
            }
            if (Time.time - _lastReversalTime > _settings.PistonReversalWindowSeconds) _recentReversals = 0;

            float effectiveDeepCm = Mathf.Min(_settings.DeepStrokeCm,
                pathLength * 100f * _settings.DeepPathCapFraction);
            if (direction > 0 && !_deepFiredThisStroke &&
                _strokeCm >= effectiveDeepCm && speed >= _settings.DeepMinSpeed &&
                depth >= pathLength * _settings.DeepEndFraction)
            {
                _deepFiredThisStroke = true;
                _deepPulse = 1f;
                _deepReturning = true;
                if (MotionTraceEnabled) TraceVibeEvent("DEEP_TRIGGER", "strokeCm=" + TraceNumber(_strokeCm)
                    + " depthM=" + TraceNumber(depth) + " speedMps=" + TraceNumber(speed)
                    + " returnSeconds=" + TraceNumber(_settings.DeepReturnSeconds));
                _deepHoldUntil = Time.time + _settings.DeepHoldSeconds;
                if (_settings.DeepVoiceEnabled && !_ogazmCueRequestedThisEntry &&
                    !MainGameVoiceFaceEventBridge.PublicApi.IsOgazmCuePlaying())
                    MainGameVoiceFaceEventBridge.PublicApi.TryRequestPriorityDeepCue(Guid);
            }
            else _deepPulse = Mathf.MoveTowards(_deepPulse, 0f,
                dt / _settings.DeepReturnSeconds);

            bool fast = _recentReversals >= _settings.PistonMinReversals && speed >= _settings.FastMinSpeed;
            bool hardQualified = _hardWindowDistanceCm >= _settings.HardPistonDistanceCm &&
                speed >= _settings.HardPistonMinSpeed &&
                _hardReversalTimes.Count >= _settings.HardPistonMinReversals;
            if (hardQualified) { _hardPistonActive = true; _hardLastQualifiedAt = now; }
            else if (_hardPistonActive && now - _hardLastQualifiedAt >= _settings.HardPistonReleaseSeconds)
                _hardPistonActive = false;
            if ((fast || _hardPistonActive) && !_ogazmCueRequestedThisEntry &&
                (!_pistonPlayedThisEntry || (!_hardPistonActive && !_hardVoicePending && !_ogazmVoicePending &&
                    now >= _nextPistonVoiceAttemptAt))) _pistonVoicePending = true;
            if (_hardPistonActive && !_ogazmCueRequestedThisEntry &&
                (!_hardPlayedThisEntry || (!_ogazmVoicePending &&
                    now >= _nextHardPistonVoiceAttemptAt)))
                _hardVoicePending = true;
            _fastPulse = fast ? (direction > 0 ? 1f : -_settings.FastPullReactionRatio)
                              : Mathf.MoveTowards(_fastPulse, 0f,
                                  dt * _settings.FastReactionReturnPerSecond);
            bool slow = speed >= _settings.AibuMinMotionSpeed &&
                        speed <= _settings.SlowMaxSpeed && Time.time >= _deepHoldUntil && !fast;
            _slowSeconds = slow ? _slowSeconds + dt : 0f;
            _motionSlowActive = _slowSeconds >= _settings.SlowConfirmSeconds ? 1f : 0f;

            // The rigid mesh tip gives the actual hand-driven approach vector; FK bending
            // alone never changes this direction or earns movement distance.
            if (_hasRigidTip && direction > 0 && _female != null)
            {
                Vector3 worldDelta = rigidTip - _lastRigidTip;
                Vector3 right = Vector3.ProjectOnPlane(_female.transform.right, axis);
                if (right.sqrMagnitude > 0.0001f)
                {
                    right.Normalize();
                    float forward = Mathf.Max(0.0001f, Vector3.Dot(worldDelta, axis));
                    float lateral = Vector3.Dot(worldDelta, right);
                    float angle = Mathf.Clamp(Mathf.Atan2(lateral, forward) * Mathf.Rad2Deg,
                        -_settings.BodyMaxAngleDeg, _settings.BodyMaxAngleDeg);
                    _motionPushDirection = (axis + right * Mathf.Tan(angle * Mathf.Deg2Rad)).normalized;
                }
            }
            if (_motionPushDirection.sqrMagnitude < 0.5f) _motionPushDirection = axis;
            _lastRigidTip = rigidTip;
            _hasRigidTip = true;

            float follow = Mathf.Clamp01(Mathf.Max(0f, depth) / pathLength) *
                _settings.BodyFollowMetres * (_motionSlowActive > 0f ? _settings.SlowBodyFollowRatio : 1f);
            float target = (follow + _fastPulse * _settings.FastReactionMetres +
                _deepPulse * _settings.DeepReactionMetres) * (1f + gauge * _settings.MotionGaugeBoost);
            target = Mathf.Clamp(target, -_settings.BodyOffsetLimitMetres, _settings.BodyOffsetLimitMetres);
            float bodySpeed = _deepReturning && target < _motionBodyOffset
                ? _settings.DeepReactionMetres / _settings.DeepReturnSeconds
                : target < _motionBodyOffset ? _settings.BodyReturnSpeed : _settings.BodyFollowSpeed;
            _motionBodyOffset = Mathf.MoveTowards(_motionBodyOffset, target, dt * bodySpeed);
            if (_deepPulse <= 0f && Mathf.Abs(_motionBodyOffset - target) < 0.0001f)
                _deepReturning = false;
            _motionUpperOffset = Mathf.MoveTowards(_motionUpperOffset,
                _motionBodyOffset * _settings.UpperBodyRatio,
                dt * _settings.UpperBodyFollowSpeed);
            TraceMovementSample(signedTravel, speed, depth, pathLength);
        }

        private void UpdateOgazmCueTrigger(bool mayTrigger)
        {
            if (_settings == null || !_settings.MotionGaugeEnabled) return;
            float gauge01 = Mathf.Clamp01(_motionGaugeCm / _settings.MotionGaugeFullCm);
            if (gauge01 <= _settings.OgazmExitGaugeFraction)
                _ogazmCueRequestedThisEntry = false;
            if (!mayTrigger || gauge01 < _settings.OgazmEnterGaugeFraction ||
                _ogazmCueRequestedThisEntry) return;
            if (!_settings.OgazmVoiceEnabled) return;
            _ogazmVoicePending = true;
        }

        private void DispatchPendingSpecialVoices()
        {
            if (_scene == null || _scene.flags == null || _scene.flags.mode != HFlag.EMode.aibu ||
                _model == null || _settings == null || !_settings.Enabled) return;
            float now = Time.unscaledTime;
            if (_pistonVoicePending)
            {
                if (now < _nextPistonVoiceAttemptAt) return;
                bool accepted = MainGameVoiceFaceEventBridge.PublicApi.TryRequestContinuousPistonCue(Guid,
                    _settings.PistonVoiceVolume, _settings.PistonVoiceCooldownSeconds);
                _nextPistonVoiceAttemptAt = now + (accepted
                    ? _settings.PistonAcceptedRetrySeconds : _settings.PistonRejectedRetrySeconds);
                if (accepted) { _pistonVoicePending = false; _pistonPlayedThisEntry = true; }
                LogMotionEvent("VOICE_REQUEST", "accepted=" + accepted);
                return;
            }
            if (_hardVoicePending)
            {
                if (now < _nextHardPistonVoiceAttemptAt) return;
                bool accepted = MainGameVoiceFaceEventBridge.PublicApi.TryRequestHardPistonCue(Guid,
                    _settings.HardPistonVoiceVolume, _settings.HardPistonVoiceCooldownSeconds);
                _nextHardPistonVoiceAttemptAt = now + (accepted
                    ? _settings.HardPistonAcceptedRetrySeconds : _settings.HardPistonRejectedRetrySeconds);
                if (accepted) { _hardVoicePending = false; _hardPlayedThisEntry = true; }
                LogMotionEvent("HARD_VOICE_REQUEST", "accepted=" + accepted);
                return;
            }
            if (_ogazmVoicePending)
            {
                bool accepted = MainGameVoiceFaceEventBridge.PublicApi.TryRequestOgazmCue(Guid,
                    _settings.OgazmVoiceVolume, _settings.OgazmVoiceCooldownSeconds);
                if (accepted) { _ogazmVoicePending = false; _ogazmCueRequestedThisEntry = true; }
                LogMotionEvent("OGAZM_VOICE_REQUEST", "accepted=" + accepted);
            }
        }
    }
}
