using HarmonyLib;
using System.Reflection;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameBeatSyncSpeed
{
    // ── WaitSpeedProc — speedCalc/speed を完全乗っ取り（Sonyu/Houshi）─
    [HarmonyPatch(typeof(HFlag), "WaitSpeedProc",
        new[] { typeof(bool), typeof(AnimationCurve) })]
    internal static class WaitSpeedProcPatch
    {
        private static float _nextLog;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HFlag __instance, AnimationCurve _curve)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return true;
            if (!Plugin.IsActive)   { PatchHelpers.LogPatchSkip("WaitSpeedProc", "IsActive=false"); return true; }
            if (__instance == null) return true;

            float intensity = Plugin.CurrentIntensity01;
            if (intensity < 0f) return true;

            __instance.speedCalc   = intensity;
            __instance.speed       = _curve != null ? _curve.Evaluate(intensity) : intensity;
            __instance.speedUpClac = new Vector2(intensity, intensity);
            __instance.timeNoClick = 0f;

            if (UnityEngine.Time.unscaledTime >= _nextLog)
            {
                _nextLog = UnityEngine.Time.unscaledTime + 1f;
                Plugin.Instance?.LogInfo($"[patch] WaitSpeedProc hijacked speedCalc={intensity:0.###} speed={__instance.speed:0.###}");
            }

            return false; // 元メソッドをスキップ
        }
    }

    // ── WaitSpeedProcAibu — Aibu 系乗っ取り ──────────────────────────
    [HarmonyPatch(typeof(HFlag), "WaitSpeedProcAibu")]
    internal static class WaitSpeedProcAibuPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HFlag __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return true;
            if (!Plugin.IsActive)   return true;
            if (__instance == null) return true;

            float intensity = Plugin.CurrentIntensity01;
            if (intensity < 0f) return true;

            float speedCap = __instance.speedMaxBody > 0f ? __instance.speedMaxBody : 1f;
            float desired  = speedCap * intensity;

            __instance.speed       = desired;
            __instance.speedItem   = desired;
            __instance.speedUpClac = new Vector2(desired, desired);
            __instance.timeNoClick = 0f;

            return false;
        }
    }

    // ── HSceneProc.Update Postfix — 毎フレーム gauge を強制反映 ────────
    [HarmonyPatch(typeof(HSceneProc), "Update")]
    internal static class HSceneProcUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!Plugin.IsActive || __instance?.flags == null) return;
            PatchHelpers.ForceGaugeOnFlags(__instance.flags);
        }
    }

    // ── HSprite.Update Postfix — 速度ゲージ UI も合わせる ───────────────
    [HarmonyPatch(typeof(HSprite), "Update")]
    internal static class HSpriteUpdatePatch
    {
        private static readonly FieldInfo FlagsField      = AccessTools.Field(typeof(HSprite), "flags");
        private static readonly FieldInfo ImageSpeedField = AccessTools.Field(typeof(HSprite), "imageSpeed");

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HSprite __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!Plugin.IsActive || __instance == null) return;

            var flags = FlagsField?.GetValue(__instance) as HFlag;
            if (flags == null) return;

            PatchHelpers.ForceGaugeOnFlags(flags);

            object imageSpeed = ImageSpeedField?.GetValue(__instance);
            if (imageSpeed == null) return;

            var fillProp = imageSpeed.GetType().GetProperty("fillAmount",
                BindingFlags.Instance | BindingFlags.Public);
            if (fillProp == null || !fillProp.CanWrite) return;

            float fill = flags.mode == HFlag.EMode.aibu
                ? Mathf.InverseLerp(0f, Mathf.Max(0.0001f, flags.speedMaxAibuBody), flags.speed)
                : Mathf.Clamp01(flags.speedCalc);

            fillProp.SetValue(imageSpeed, fill, null);
        }
    }

    // ── 共通ヘルパー ─────────────────────────────────────────────────────
    internal static class PatchHelpers
    {
        private static float _skipLogNext;
        internal static void LogPatchSkip(string patch, string reason)
        {
            if (UnityEngine.Time.unscaledTime < _skipLogNext) return;
            _skipLogNext = UnityEngine.Time.unscaledTime + 2f;
            Plugin.Instance?.LogInfo($"[patch-skip] {patch}: {reason}");
        }

        internal static void ForceGaugeOnFlags(HFlag flags)
        {
            if (flags == null) return;
            float intensity = Plugin.CurrentIntensity01;
            if (intensity < 0f) return;

            flags.speedCalc   = intensity;
            flags.speedUpClac = new Vector2(intensity, intensity);
            flags.timeNoClick = 0f;

            if (flags.mode == HFlag.EMode.aibu)
            {
                float speedCap = flags.speedMaxBody > 0f ? flags.speedMaxBody : 1f;
                float desired  = speedCap * intensity;
                flags.speed     = desired;
                flags.speedItem = desired;
            }
        }

        internal static void UpdateVoiceSpeedMotion(HFlag flags)
        {
            if (flags?.voice == null) return;
            float threshold = flags.speedMaxBody * flags.speedVoiceChangeSpeedRate;
            if (!flags.voice.speedMotion && flags.speedCalc > threshold)
                flags.voice.speedMotion = true;
            else if (flags.voice.speedMotion && flags.speedCalc < flags.speedMaxBody - threshold)
                flags.voice.speedMotion = false;
        }
    }
}
