using System;
using System.Reflection;
using UnityEngine;

namespace MainGameBeatSyncSpeed
{
    public partial class Plugin
    {
        private readonly float[] _tapTimes = new float[64];
        private int   _tapCount    = 0;
        private float _lastTapTime = -1f;

        private const float TapTimeoutSec = 2f;

        private void UpdateTapTempo()
        {
            float now = Time.unscaledTime;

            // タイムアウト判定（最後のタップから2秒経過）
            if (_lastTapTime >= 0f && now - _lastTapTime >= TapTimeoutSec)
            {
                if (_tapCount >= 2)
                    FinalizeTapBpm();
                else
                    LogInfo("[tap] タップ1回のみ → リセット");

                _tapCount    = 0;
                _lastTapTime = -1f;
            }

            if (!Input.GetKeyDown(KeyCode.RightControl)) return;

            if (_tapCount < _tapTimes.Length)
                _tapTimes[_tapCount] = now;

            _tapCount++;
            _lastTapTime = now;

            if (_tapCount == 1)
                LogInfo("[tap] 開始 - 右CTRLを続けてタップ");
            else
                LogInfo($"[tap] {_tapCount}回目");
        }

        private void FinalizeTapBpm()
        {
            int used      = Mathf.Min(_tapCount, _tapTimes.Length);
            float total   = _tapTimes[used - 1] - _tapTimes[0];
            if (total <= 0f) return;

            int bpm = Mathf.RoundToInt((used - 1) / total * 60f);
            bpm = Mathf.Clamp(bpm, 1, 999);

            _cfgBpm.Value = bpm;
            LogInfo($"[tap] BPM確定: {bpm} ({used}回 / {total:0.##}s)");
            InvalidateAnalysis();
            PersistCurrentSongBpm(bpm, "tap-tempo");

            TryApplyToSpeedLimitBreak(bpm, "tap-tempo");
        }

        private void TryApplyToSpeedLimitBreak(float bpmMax, string reason)
        {
            try
            {
                var type = Type.GetType("MainGameSpeedLimitBreak.Plugin, MainGameSpeedLimitBreak");
                if (type == null) return;
                var method = type.GetMethod("ApplyTapBpm",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null) return;
                method.Invoke(null, new object[] { bpmMax });
                LogInfo($"[speedlimit] BPM送信 reason={reason} bpm={bpmMax}");
            }
            catch (Exception ex)
            {
                LogWarn($"[speedlimit] BPM送信失敗 reason={reason} error={ex.Message}");
            }
        }
    }
}
