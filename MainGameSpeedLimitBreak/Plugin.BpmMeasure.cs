using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private readonly bool _enableBpmMeasureLogs = false;

        private void StartBpmMeasure(BpmMeasureTarget target)
        {
            if (target == BpmMeasureTarget.None)
                return;

            var s = Settings;
            if (s == null)
            {
                ShowUiNotice("settings未読込");
                return;
            }

            if (!TryGetFemaleAnimatorState(out var stateInfo))
            {
                ShowUiNotice("計算開始失敗: 女アニメ状態を取得できない");
                return;
            }

            _bpmMeasure.Running = true;
            _bpmMeasure.Target = target;
            _bpmMeasure.StateHash = stateInfo.fullPathHash;
            _bpmMeasure.LastNorm = stateInfo.normalizedTime;
            _bpmMeasure.LastSampleTime = Time.realtimeSinceStartup;
            _bpmMeasure.AccumNorm = 0f;
            _bpmMeasure.AccumSec = 0f;

            ShowUiNotice($"計算開始: {GetMeasureTargetLabel(target)}");
            LogBpmMeasureInfo(
                $"bpm measure start target={GetMeasureTargetLabel(target)} stateHash={stateInfo.fullPathHash} " +
                $"window={s.BpmMeasureWindowSec:0.###} strokesPerLoop={s.BpmMeasureStrokesPerLoop:0.###}");
        }

        private void UpdateBpmMeasure()
        {
            if (!_bpmMeasure.Running)
                return;

            var s = Settings;
            if (s == null)
            {
                StopBpmMeasure("settings-null", keepResult: true);
                return;
            }

            if (!TryGetFemaleAnimatorState(out var stateInfo))
            {
                StopBpmMeasure("animator-state-missing", keepResult: true);
                ShowUiNotice("計算停止: 女アニメ状態を取得できない");
                return;
            }

            if (s.BpmMeasureAbortOnStateChange && stateInfo.fullPathHash != _bpmMeasure.StateHash)
            {
                StopBpmMeasure("state-changed", keepResult: true);
                ShowUiNotice("計算停止: アニメ状態が変化");
                return;
            }

            float now = Time.realtimeSinceStartup;
            float dt = now - _bpmMeasure.LastSampleTime;
            float dNorm = stateInfo.normalizedTime - _bpmMeasure.LastNorm;

            _bpmMeasure.LastSampleTime = now;
            _bpmMeasure.LastNorm = stateInfo.normalizedTime;

            if (dt <= 0f)
                return;

            if (dNorm < s.BpmMeasureNegativeDeltaResetThreshold)
            {
                _bpmMeasure.AccumNorm = 0f;
                _bpmMeasure.AccumSec = 0f;
                return;
            }

            if (dNorm < 0f)
                dNorm = 0f;

            _bpmMeasure.AccumNorm += dNorm;
            _bpmMeasure.AccumSec += dt;

            if (_bpmMeasure.AccumSec >= s.BpmMeasureWindowSec)
            {
                CompleteBpmMeasure();
            }
        }

        private void CompleteBpmMeasure()
        {
            var s = Settings;
            if (s == null)
            {
                StopBpmMeasure("settings-null", keepResult: true);
                return;
            }

            float sec = _bpmMeasure.AccumSec;
            float norm = _bpmMeasure.AccumNorm;
            if (sec < s.BpmMeasureMinAccumSec || norm <= 0f)
            {
                StopBpmMeasure("insufficient-samples", keepResult: true);
                ShowUiNotice("計算失敗: サンプル不足");
                return;
            }

            float loopsPerSec = norm / sec;
            float bpm = loopsPerSec * s.BpmMeasureStrokesPerLoop * 60f;

            if (_bpmMeasure.Target == BpmMeasureTarget.Min)
                _measuredMinBpm = bpm;
            else if (_bpmMeasure.Target == BpmMeasureTarget.Max)
                _measuredMaxBpm = bpm;

            string targetLabel = GetMeasureTargetLabel(_bpmMeasure.Target);
            StopBpmMeasure("completed", keepResult: true);
            ShowUiNotice($"計算完了 {targetLabel}: {bpm:0.##} BPM");
            LogBpmMeasureInfo(
                $"bpm measure completed target={targetLabel} bpm={bpm:0.##} " +
                $"loopsPerSec={loopsPerSec:0.###} window={sec:0.###} normDelta={norm:0.###}");
        }

        private void StopBpmMeasure(string reason, bool keepResult)
        {
            if (_bpmMeasure.Running)
                LogBpmMeasureInfo($"bpm measure stop reason={reason} target={GetMeasureTargetLabel(_bpmMeasure.Target)}");

            _bpmMeasure.Running = false;
            _bpmMeasure.Target = BpmMeasureTarget.None;
            _bpmMeasure.StateHash = 0;
            _bpmMeasure.LastNorm = 0f;
            _bpmMeasure.LastSampleTime = 0f;
            _bpmMeasure.AccumNorm = 0f;
            _bpmMeasure.AccumSec = 0f;

            if (!keepResult)
            {
                _measuredMinBpm = -1f;
                _measuredMaxBpm = -1f;
            }
        }

        private bool TryGetFemaleAnimatorState(out AnimatorStateInfo stateInfo)
        {
            stateInfo = default;
            if (!TryGetPrimaryFemale(out var female))
                return false;

            stateInfo = female.getAnimatorStateInfo(0);
            return true;
        }

        private bool TryGetPrimaryFemale(out ChaControl female)
        {
            female = null;
            if (_hSceneProc == null)
                return false;

            var females = LstFemaleField?.GetValue(_hSceneProc) as List<ChaControl>;
            if (females == null || females.Count == 0)
                return false;

            for (int i = 0; i < females.Count; i++)
            {
                if (females[i] != null)
                {
                    female = females[i];
                    break;
                }
            }

            return female != null;
        }

        private bool TryGetCurrentAnimationName(out string animationName)
        {
            animationName = null;
            if (!TryGetPrimaryFemale(out var female))
                return false;

            var animator = female.animBody;
            if (animator != null)
            {
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                {
                    string clipName = clips[0].clip.name;
                    if (!string.IsNullOrWhiteSpace(clipName))
                    {
                        animationName = clipName.Trim();
                        return true;
                    }
                }
            }

            var state = female.getAnimatorStateInfo(0);
            if (state.shortNameHash != 0)
            {
                animationName = $"state_{state.shortNameHash}";
                return true;
            }

            return false;
        }

        private static string BuildAnimationFolderName(string animationName)
        {
            if (string.IsNullOrWhiteSpace(animationName))
                return "unknown";

            string folder = animationName.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                folder = folder.Replace(invalid[i], '_');

            folder = folder.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            if (string.IsNullOrWhiteSpace(folder))
                return "unknown";

            return folder;
        }

        private static string GetMeasureTargetLabel(BpmMeasureTarget target)
        {
            switch (target)
            {
                case BpmMeasureTarget.Min:
                    return "最小値";
                case BpmMeasureTarget.Max:
                    return "最大値";
                default:
                    return "なし";
            }
        }

        private static string FormatMeasuredBpm(float bpm)
        {
            return bpm > 0f ? $"{bpm:0.##} BPM" : "-";
        }

        private void LogBpmMeasureInfo(string message)
        {
            if (!_enableBpmMeasureLogs)
                return;

            LogInfo(message);
        }
    }
}
