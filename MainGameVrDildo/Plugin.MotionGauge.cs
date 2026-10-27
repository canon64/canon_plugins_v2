using System;
using HarmonyLib;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private void InstallMotionGaugeHook()
        {
            try
            {
                _motionGaugeHarmony = new Harmony(Guid + ".motion-gauge");
                _motionGaugeHarmony.Patch(AccessTools.Method(typeof(HSprite), "Update"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterHSpriteUpdate)));
                _motionGaugeHarmony.Patch(AccessTools.Method(typeof(HAibu), "Proc"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterAibuProc)));
                TraceVibeEvent("GAUGE_HOOK_READY", "HSprite.Update and HAibu.Proc postfix installed");
            }
            catch (Exception ex)
            {
                TraceVibeEvent("GAUGE_HOOK_ERROR", ex.ToString());
                Logger.LogWarning("Movement-distance gauge hook unavailable: " + ex.Message);
            }
        }

        private static void AfterHSpriteUpdate(HSprite __instance)
        {
            Plugin p = Instance;
            if (p != null && p.MotionTraceEnabled) p._traceGaugeCalls++;
            if (p == null || p._settings == null || !p._settings.Enabled ||
                !p._settings.MotionGaugeEnabled || p._model == null || p._scene == null ||
                p._scene.flags == null || p._scene.flags.mode != HFlag.EMode.aibu ||
                __instance == null || __instance.imageSpeed == null)
                return;
            // HSprite has already drawn the native speed gauge this frame. During aibu
            // the same lower-right fill shows recent Vibe travel instead.
            __instance.imageSpeed.fillAmount = Mathf.Clamp01(
                p._motionGaugeCm / p._settings.MotionGaugeFullCm);
            if (p.MotionTraceEnabled)
            {
                p._traceDisplayedGauge = __instance.imageSpeed.fillAmount;
                p._traceGaugeFrame = Time.frameCount;
            }
        }

        private static void AfterAibuProc(HAibu __instance)
        {
            Plugin p = Instance;
            p?.TraceAibuEnter();
            if (p == null || p._settings == null || !p._settings.Enabled ||
                !p._settings.MotionGaugeEnabled || p._model == null ||
                p._scene == null || p._scene.flags == null ||
                p._scene.flags.mode != HFlag.EMode.aibu) return;
            // HAibu.Proc derives the animation blend parameter from the wheel-controlled
            // speed every frame. The right-hand gauge is Vibe travel instead, so combine
            // both sources after the native calculation. Keep the stronger one so wheel
            // input remains effective even when the travel gauge is empty.
            float gauge01 = Mathf.Clamp01(p._motionGaugeCm / p._settings.MotionGaugeFullCm);
            float motion = Mathf.Max(p._scene.flags.motion,
                gauge01 * p._settings.MotionGaugeAnimationStrengthWeight);
            p._scene.flags.motion = motion;
            if (p._female != null && p._female.animBody != null)
                p._female.animBody.SetFloat("motion", motion);
            p.TraceAibuApplied(gauge01, motion);

            var pose = p._scene.flags.nowAnimationInfo;
            if (pose == null || pose.nameAnimation == null ||
                !pose.nameAnimation.EndsWith("【クローン】", StringComparison.Ordinal) ||
                (pose.id != 0 && pose.id != 1 && pose.id != 7)) return;
            ChaControl female = p._female;
            if (female == null || female.animBody == null) return;
            float multiplier = Mathf.Lerp(p._settings.CloneAnimationMinMultiplier,
                p._settings.CloneAnimationMaxMultiplier,
                gauge01 * p._settings.CloneAnimationGaugeWeight);
            female.animBody.SetFloat("speedBody", multiplier);
        }
    }
}
