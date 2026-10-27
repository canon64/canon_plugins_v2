using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private static readonly FieldInfo HSpriteFlagsField = AccessTools.Field(typeof(HSprite), "flags");
        private static readonly FieldInfo HSpriteImageSpeedField = AccessTools.Field(typeof(HSprite), "imageSpeed");

        internal void ForceTimelineGaugeOnFlags(HFlag flags, string sourceTag)
        {
            var s = Settings;
            if (s == null)
            {
                TraceTimelineSkip(sourceTag, "settings-null");
                return;
            }

            if (!s.Enabled)
            {
                TraceTimelineSkip(sourceTag, "plugin-disabled");
                return;
            }

            if (flags == null)
            {
                TraceTimelineSkip(sourceTag, "flags-null");
                return;
            }

            if (!TryGetTimelineGauge01(out float gauge01, out string gaugeReason))
            {
                TraceTimelineSkip(sourceTag, gaugeReason ?? "gauge-unavailable");
                return;
            }

            GetEffectiveSourceRange(out float fromMin, out float fromMax);
            fromMax = Mathf.Max(fromMin + 0.0001f, fromMax);

            flags.speedCalc = gauge01;
            flags.speedUpClac = new Vector2(gauge01, gauge01);
            flags.timeNoClick = 0f;

            if (flags.mode == HFlag.EMode.aibu)
            {
                float speedCap = Mathf.Max(0.0001f, flags.speedMaxBody > 0f ? flags.speedMaxBody : fromMax);
                float desired = speedCap * gauge01;
                flags.speed = desired;
                flags.speedItem = desired;
            }
            else
            {
                AnimationCurve curve = ResolveCurveByMode(flags);
                float speed = curve != null
                    ? curve.Evaluate(gauge01)
                    : Mathf.Lerp(fromMin, fromMax, gauge01);
                flags.speed = Mathf.Clamp(speed, fromMin, fromMax);
            }

            TraceTimelineApply(sourceTag, flags, gauge01);
        }

        internal void ForceTimelineGaugeOnSprite(HSprite sprite)
        {
            if (sprite == null)
            {
                TraceTimelineSkip("hsprite-update", "sprite-null");
                return;
            }

            if (!TryGetTimelineGauge01(out float gauge01, out string gaugeReason))
            {
                TraceTimelineSkip("hsprite-update", gaugeReason ?? "gauge-unavailable");
                return;
            }

            var flags = HSpriteFlagsField?.GetValue(sprite) as HFlag;
            if (flags == null)
            {
                TraceTimelineSkip("hsprite-update", "sprite-flags-null");
                return;
            }

            ForceTimelineGaugeOnFlags(flags, "hsprite-update");

            object image = HSpriteImageSpeedField?.GetValue(sprite);
            if (image == null)
            {
                TraceTimelineSkip("hsprite-update", "imageSpeed-null");
                return;
            }

            var fillProp = image.GetType().GetProperty("fillAmount", BindingFlags.Instance | BindingFlags.Public);
            if (fillProp == null || !fillProp.CanWrite)
            {
                TraceTimelineSkip("hsprite-update", "fillAmount-not-writable");
                return;
            }

            float fillAmount = flags.mode == HFlag.EMode.aibu
                ? Mathf.InverseLerp(0f, Mathf.Max(0.0001f, flags.speedMaxAibuBody), flags.speed)
                : Mathf.Clamp01(gauge01);
            fillProp.SetValue(image, fillAmount, null);
            TracePatchCall("HSprite.FillAmount.Override", flags);
        }

        private bool TryGetTimelineGauge01(out float gauge01, out string reason)
        {
            gauge01 = -1f;
            reason = null;
            if (!TryCheckTimelineHijackActive(out reason))
                return false;

            var s = Settings;
            if (s == null)
            {
                reason = "settings-null";
                return false;
            }

            if (_timelineGaugeOverrideEnabled && _timelineGaugeOverride01 >= 0f)
            {
                gauge01 = Mathf.Clamp01(_timelineGaugeOverride01);
                reason = "override";
                return true;
            }

            GetEffectiveSourceRange(out float effMin, out float effMax);
            effMax = Mathf.Max(effMin + 0.0001f, effMax);
            float sourceForGauge = Mathf.Clamp(s.TargetMaxSpeed, effMin, effMax);
            gauge01 = Mathf.Clamp01(Mathf.InverseLerp(effMin, effMax, sourceForGauge));
            reason = "derived-target-max";
            return true;
        }

        private static AnimationCurve ResolveCurveByMode(HFlag flags)
        {
            if (flags == null)
                return null;

            switch (flags.mode)
            {
                case HFlag.EMode.houshi:
                case HFlag.EMode.houshi3P:
                case HFlag.EMode.houshi3PMMF:
                    return flags.speedHoushiCurve;
                case HFlag.EMode.sonyu:
                case HFlag.EMode.sonyu3P:
                case HFlag.EMode.sonyu3PMMF:
                    return flags.speedSonyuCurve;
                default:
                    return flags.speedSonyuCurve;
            }
        }
    }
}
