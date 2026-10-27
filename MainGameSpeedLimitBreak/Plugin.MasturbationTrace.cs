using System;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private sealed class MasturbationTraceSnapshot
        {
            public string Mode = string.Empty;
            public int AnimationInfoId = -1;
            public string AnimationInfoMode = string.Empty;
            public string AnimationInfoName = string.Empty;
            public int StateFullPathHash;
            public int StateShortNameHash;
            public float StateNormalizedTime;
            public int StateLoopIndex;
            public string ClipName = string.Empty;
            public float ClipLengthSec;
            public float ClipWeight;
            public float AnimatorSpeed = 1f;
            public float Speed;
            public float SpeedCalc;
            public float SpeedMaxBody;
            public float SpeedMaxAibuBody;
            public string Click = string.Empty;
            public string TransitionKey = string.Empty;
        }

        private bool _masturbationTraceActive;
        private string _masturbationTraceTransitionKey = string.Empty;
        private int _masturbationTraceLastLoopIndex = -1;
        private float _masturbationTraceNextSnapshotTime;

        private void ResetMasturbationTransitionTraceState()
        {
            _masturbationTraceActive = false;
            _masturbationTraceTransitionKey = string.Empty;
            _masturbationTraceLastLoopIndex = -1;
            _masturbationTraceNextSnapshotTime = 0f;
        }

        private void UpdateMasturbationStateTrace()
        {
            var s = Settings;
            if (s == null || !s.EnableMasturbationTransitionTrace)
            {
                ResetMasturbationTransitionTraceState();
                return;
            }

            HFlag flags = _hSceneProc?.flags;
            if (flags == null)
            {
                if (_masturbationTraceActive)
                {
                    LogInfo("[mast-trace] exit reason=flags-null");
                }

                ResetMasturbationTransitionTraceState();
                return;
            }

            if (!IsMasturbationMode(flags.mode))
            {
                if (_masturbationTraceActive)
                {
                    LogInfo($"[mast-trace] exit mode={flags.mode}");
                }

                ResetMasturbationTransitionTraceState();
                return;
            }

            if (!TryCaptureMasturbationTraceSnapshot(flags, out MasturbationTraceSnapshot snapshot, out string reason))
            {
                float failInterval = Mathf.Max(0.1f, s.MasturbationTraceIntervalSec);
                if (!_masturbationTraceActive || Time.unscaledTime >= _masturbationTraceNextSnapshotTime)
                {
                    LogInfo($"[mast-trace] capture-failed reason={reason ?? "unknown"} mode={flags.mode}");
                    _masturbationTraceNextSnapshotTime = Time.unscaledTime + failInterval;
                }

                _masturbationTraceActive = true;
                return;
            }

            if (!_masturbationTraceActive)
            {
                _masturbationTraceActive = true;
                _masturbationTraceTransitionKey = string.Empty;
                _masturbationTraceLastLoopIndex = snapshot.StateLoopIndex;
                _masturbationTraceNextSnapshotTime = 0f;
                LogInfo(FormatMasturbationTraceSnapshot("enter", snapshot));
            }

            if (!string.Equals(_masturbationTraceTransitionKey, snapshot.TransitionKey, StringComparison.Ordinal))
            {
                LogInfo(FormatMasturbationTraceSnapshot("transition", snapshot));
                _masturbationTraceTransitionKey = snapshot.TransitionKey;
            }

            if (_masturbationTraceLastLoopIndex != snapshot.StateLoopIndex)
            {
                LogInfo(FormatMasturbationTraceSnapshot("loop-changed", snapshot));
                _masturbationTraceLastLoopIndex = snapshot.StateLoopIndex;
            }
        }

        private bool TryCaptureMasturbationTraceSnapshot(HFlag flags, out MasturbationTraceSnapshot snapshot, out string reason)
        {
            snapshot = null;
            reason = null;
            if (flags == null)
            {
                reason = "flags-null";
                return false;
            }

            var data = new MasturbationTraceSnapshot
            {
                Mode = flags.mode.ToString(),
                Speed = flags.speed,
                SpeedCalc = flags.speedCalc,
                SpeedMaxBody = flags.speedMaxBody,
                SpeedMaxAibuBody = flags.speedMaxAibuBody,
                Click = flags.click.ToString()
            };

            HSceneProc.AnimationListInfo nowInfo = flags.nowAnimationInfo;
            if (nowInfo != null)
            {
                data.AnimationInfoId = nowInfo.id;
                data.AnimationInfoMode = nowInfo.mode.ToString();
                data.AnimationInfoName = NormalizeMasturbationTraceToken(nowInfo.nameAnimation);
            }
            else
            {
                data.AnimationInfoMode = "null";
                data.AnimationInfoName = string.Empty;
            }

            if (!TryGetPrimaryFemale(out ChaControl female) || female == null)
            {
                reason = "primary-female-missing";
                return false;
            }

            AnimatorStateInfo state = female.getAnimatorStateInfo(0);
            data.StateFullPathHash = state.fullPathHash;
            data.StateShortNameHash = state.shortNameHash;
            data.StateNormalizedTime = state.normalizedTime;
            data.StateLoopIndex = Mathf.FloorToInt(state.normalizedTime);

            Animator animator = female.animBody;
            if (animator != null)
            {
                data.AnimatorSpeed = animator.speed;
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                {
                    data.ClipName = NormalizeMasturbationTraceToken(clips[0].clip.name);
                    data.ClipLengthSec = clips[0].clip.length;
                    data.ClipWeight = clips[0].weight;
                }
            }

            data.TransitionKey = string.Concat(
                data.AnimationInfoId.ToString(),
                "|",
                data.AnimationInfoMode,
                "|",
                data.AnimationInfoName,
                "|",
                data.StateFullPathHash.ToString(),
                "|",
                data.ClipName);

            snapshot = data;
            return true;
        }

        private static bool IsMasturbationMode(HFlag.EMode mode)
        {
            string name = mode.ToString();
            return string.Equals(name, "masturbation", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "aibu", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMasturbationTraceToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            return raw.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string FormatMasturbationTraceSnapshot(string tag, MasturbationTraceSnapshot s)
        {
            if (s == null)
            {
                return $"[mast-trace] {tag} snapshot=null";
            }

            return
                $"[mast-trace] {tag} " +
                $"mode={s.Mode} " +
                $"nowInfo(id={s.AnimationInfoId},mode={s.AnimationInfoMode},name={s.AnimationInfoName}) " +
                $"clip(name={s.ClipName},len={s.ClipLengthSec:0.###},w={s.ClipWeight:0.###}) " +
                $"state(full={s.StateFullPathHash},short={s.StateShortNameHash},loop={s.StateLoopIndex},norm={s.StateNormalizedTime:0.###}) " +
                $"animSpeed={s.AnimatorSpeed:0.###} " +
                $"flags(speed={s.Speed:0.###},speedCalc={s.SpeedCalc:0.###},maxBody={s.SpeedMaxBody:0.###},maxAibu={s.SpeedMaxAibuBody:0.###},click={s.Click})";
        }
    }
}
