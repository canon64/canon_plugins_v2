using HarmonyLib;
using System;
using UnityEngine;

namespace MainGameHandySync
{
    /// <summary>
    /// Reads actual HScene animation progress (normalizedTime) for reliable 1:1 sync.
    /// One full animation loop = one full Handy stroke cycle.
    /// </summary>
    internal sealed class AnimationPhaseReader
    {
        private bool _initialized;
        private Animator _femaleAnimator;
        private int _cachedLayer = 0;

        internal float GetAnimationPhase(HSceneProc hSceneProc, HFlag hFlag)
        {
            if (hSceneProc == null || hFlag == null) return 0f;

            if (!_initialized)
            {
                TryInit(hSceneProc, hFlag);
            }

            if (_femaleAnimator == null)
            {
                // Fallback to old speed-based fake if animator not available
                return GetSpeedBasedFake(hFlag);
            }

            try
            {
                // In sonyu modes, the insertion motion progress is visible in the animator state
                AnimatorStateInfo state = _femaleAnimator.GetCurrentAnimatorStateInfo(_cachedLayer);
                float normalized = state.normalizedTime % 1.0f;
                if (normalized < 0f) normalized += 1f;

                return normalized;
            }
            catch
            {
                return GetSpeedBasedFake(hFlag);
            }
        }

        private void TryInit(HSceneProc proc, HFlag flag)
        {
            _initialized = true;

            try
            {
                // Get female character (primary for insertion animation feel)
                if (flag.lstHeroine != null && flag.lstHeroine.Count > 0)
                {
                    var heroine = flag.lstHeroine[0];
                    if (heroine != null && heroine.chaCtrl != null && heroine.chaCtrl.animBody != null)
                    {
                        _femaleAnimator = heroine.chaCtrl.animBody;
                        SimpleFileLogger.Info("AnimationPhaseReader: Using female.animBody");
                        return;
                    }
                }

                // Fallback using reflection (HSceneProc.lstFemale is not always public)
                try
                {
                    var fField = AccessTools.Field(typeof(HSceneProc), "lstFemale");
                    var list = fField?.GetValue(proc) as System.Collections.IList;
                    if (list != null && list.Count > 0)
                    {
                        var female = list[0];
                        var animProp = female?.GetType().GetProperty("animBody");
                        var anim = animProp?.GetValue(female) as Animator;
                        if (anim != null)
                        {
                            _femaleAnimator = anim;
                            SimpleFileLogger.Info("AnimationPhaseReader: Using reflected lstFemale[0].animBody");
                            return;
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn($"AnimationPhaseReader init failed: {ex.Message}");
            }
        }

        private float GetSpeedBasedFake(HFlag flag)
        {
            // Very rough fallback - not ideal but better than nothing
            float speed = Mathf.Clamp(flag.speed, 0f, 2f);
            float t = (Time.time * (0.8f + speed * 0.6f)) % 2f;
            return t > 1f ? (2f - t) : t;
        }

        internal void Reset()
        {
            _initialized = false;
            _femaleAnimator = null;
        }
    }
}
