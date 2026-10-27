// LoveMachine (Sauceke, GPLv3) からの移植。アルゴリズムは verbatim。
// 元: Controller/StrokerController.cs (HandleAnimation + GetPosition + 波形)
// 変更点: Client.LinearCmd(feature, pos, secs) → _linearCmd(pos01, secs)（= v3 /hdsp/xpt）、
//         settings → 引数フィールド、orgasm/intensity 等は簡略化（scale=1）。
using System;
using System.Collections;
using UnityEngine;

namespace MainGameHandySync.LM
{
    internal sealed class LmStroker
    {
        // position(0-1) を timeSecs かけて移動（LoveMachine の Client.LinearCmd 相当）→ v3 xpt
        private readonly Action<float, float> _linearCmd;

        public LmStroker(Action<float, float> linearCmd)
        {
            _linearCmd = linearCmd;
        }

        // 設定（当プラグインの config から毎回供給）
        public bool SmoothStroking = false;
        public int UpdatesHz = 16;
        public float ZoneMin = 0.05f;   // 0-1
        public float ZoneMax = 0.95f;   // 0-1
        public StrokingPattern Pattern = StrokingPattern.Animation;
        public float[] CustomPattern = { 0f, 1f };
        public float StrokingIntensity = 0f;

        // StrokerController.HandleAnimation (verbatim, LinearCmd だけ差し替え)
        public IEnumerator HandleAnimation(StrokeInfo strokeInfo)
        {
            int updateFrequency = UpdatesHz;
            float durationSecs = strokeInfo.DurationSecs;
            int subdivisions = 2 * (int)Mathf.Max(1f, durationSecs * updateFrequency / 2);
            subdivisions = (Pattern == StrokingPattern.Sine && subdivisions == 4) ? 2 : subdivisions;
            int segments = SmoothStroking ? subdivisions : 2;
            float startCompletion = strokeInfo.Completion;
            float nextSegmentCompletion = Mathf.Round(startCompletion * segments + 1) / segments;
            float timeToNextSegmentSecs = (nextSegmentCompletion - startCompletion) * durationSecs;
            GetStrokeZone(out float bottom, out float top);
            float currentPosition = Mathf.Lerp(bottom, top, GetPosition(startCompletion, strokeInfo));
            float nextPosition = Mathf.Lerp(bottom, top, GetPosition(nextSegmentCompletion, strokeInfo));
            bool movingUp = currentPosition < nextPosition;
            float targetPosition = movingUp ? top : bottom;
            float speed = (nextPosition - currentPosition) / timeToNextSegmentSecs;
            speed *= movingUp ? 1f : 1f + StrokingIntensity;
            float timeToTargetSecs = (targetPosition - currentPosition) / speed;
            _linearCmd(targetPosition, timeToTargetSecs);
            yield return WaitForSecondsUnscaled(timeToNextSegmentSecs - Time.deltaTime);
        }

        // StrokerController.GetPosition (verbatim)
        public float GetPosition(float x, StrokeInfo strokeInfo)
        {
            if (!SmoothStroking)
                return SineWave(x);
            switch (Pattern)
            {
                case StrokingPattern.Sine: return SineWave(x);
                case StrokingPattern.Cups: return CupsWave(x);
                case StrokingPattern.Arches: return ArchesWave(x);
                case StrokingPattern.Animation: return CustomWave(x, strokeInfo.Pattern);
                case StrokingPattern.Custom: return CustomWave(x, CustomPattern);
            }
            throw new Exception("unreachable");
        }

        private static float SineWave(float x) =>
            Mathf.InverseLerp(1f, -1f, Mathf.Cos(2 * Mathf.PI * x));

        private static float CupsWave(float x) => 1 - Mathf.Abs(Mathf.Cos(Mathf.PI * x));

        private static float ArchesWave(float x) => Mathf.Abs(Mathf.Sin(Mathf.PI * x));

        private static float CustomWave(float x, float[] pattern)
        {
            if (pattern == null || pattern.Length == 0) return SineWave(x);
            return pattern[(int)(Mathf.Repeat(x, 1f) * pattern.Length)];
        }

        private void GetStrokeZone(out float min, out float max)
        {
            // GetIntensity の scale は 1（フルレンジ）に簡略化
            min = ZoneMin;
            max = ZoneMax;
        }

        private static object WaitForSecondsUnscaled(float seconds) => Time.timeScale > 0f
            ? (object)new WaitForSeconds(Mathf.Max(0f, seconds) * Time.timeScale)
            : new WaitForSecondsRealtime(Mathf.Max(0f, seconds));
    }
}
