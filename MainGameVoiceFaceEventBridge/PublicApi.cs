using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    /// <summary>
    /// 他プラグインから呼ぶための公開エントリポイント。
    /// (本体 <see cref="Plugin"/> は internal sealed のため、ここを薄いラッパとして公開する)
    /// </summary>
    public static class PublicApi
    {
        /// <summary>Enable cue requests for a detector. Disabling fades its active cue and resumes dialogue.</summary>
        public static void SetImpactCueEnabled(string owner, bool enabled)
            => Plugin.Instance?.SetImpactCueEnabled(owner, enabled);

        /// <summary>Play a preloaded cue even without dialogue. Busy/cooldown requests are discarded.</summary>
        public static bool TryRequestImpactCue(string owner)
            => Plugin.Instance != null && Plugin.Instance.RequestImpactCue(owner);

        /// <summary>Replace all current voices with a selected deep-hit cue in H aibu.</summary>
        public static bool TryRequestPriorityDeepCue(string owner)
            => Plugin.Instance != null && Plugin.Instance.RequestPriorityDeepCue(owner);

        /// <summary>Play one manifest-backed piston voice in H aibu with lip sync and subtitles.</summary>
        public static bool TryRequestContinuousPistonCue(string owner)
            => Plugin.Instance != null && Plugin.Instance.RequestContinuousPistonCue(owner);

        public static bool TryRequestContinuousPistonCue(string owner, float volume, float cooldownSeconds)
            => Plugin.Instance != null && Plugin.Instance.RequestPistonCue(owner, false, volume, cooldownSeconds);

        public static bool TryRequestHardPistonCue(string owner, float volume, float cooldownSeconds)
            => Plugin.Instance != null && Plugin.Instance.RequestPistonCue(owner, true, volume, cooldownSeconds);

        public static bool IsHardPistonCuePlaying()
            => Plugin.Instance != null && Plugin.Instance.IsHardPistonCuePlaying;

        public static bool TryRequestOgazmCue(string owner, float volume, float cooldownSeconds)
            => Plugin.Instance != null && Plugin.Instance.RequestOgazmCue(owner, volume, cooldownSeconds);

        public static bool IsOgazmCuePlaying()
            => Plugin.Instance != null && Plugin.Instance.IsOgazmCuePlaying;


        /// <summary>
        /// 現在再生中の外部ボイス (SBV2 等) に対して、
        /// attack→hold→release 形のエンベロープで音量を一時的に持ち上げる。
        /// 再生していない / プラグイン未起動の場合は false。
        /// </summary>
        /// <param name="peakMultiplier">ピーク倍率 (1.0 = 等倍, 1.5 = +50%)</param>
        /// <param name="attackMs">立ち上がり時間 (ミリ秒)</param>
        /// <param name="holdMs">ピーク維持時間 (ミリ秒)</param>
        /// <param name="releaseMs">減衰時間 (ミリ秒)</param>
        public static bool TryRequestTransientVolumeBoost(float peakMultiplier, int attackMs, int holdMs, int releaseMs)
        {
            return TryRequestTransientVolumeBoost(peakMultiplier, attackMs, holdMs, releaseMs, 0, EasingShape.CosineInOut);
        }

        /// <summary>
        /// 拡張版: ブースト直後に silenceMs ミリ秒の無音を挿入し、AudioSource を一時 Pause する。
        /// 無音中は再生時刻が止まる (UnPause で再開)。easing で In/Out カーブを指定可能。
        /// </summary>
        public static bool TryRequestTransientVolumeBoost(
            float peakMultiplier,
            int attackMs,
            int holdMs,
            int releaseMs,
            int silenceMs,
            EasingShape easing)
        {
            return TryRequestTransientVolumeBoost(
                peakMultiplier,
                attackMs,
                holdMs,
                releaseMs,
                silenceMs,
                80,
                80,
                easing);
        }

        public static bool TryRequestTransientVolumeBoost(
            float peakMultiplier,
            int attackMs,
            int holdMs,
            int releaseMs,
            int silenceMs,
            int silenceFadeOutMs,
            int silenceFadeInMs,
            EasingShape easing)
        {
            Plugin inst = Plugin.Instance;
            if (inst == null) return false;
            return inst.RequestExternalVolumeBoost(
                peakMultiplier,
                attackMs * 0.001f,
                holdMs * 0.001f,
                releaseMs * 0.001f,
                Mathf.Max(0, silenceMs) * 0.001f,
                Mathf.Max(0, silenceFadeOutMs) * 0.001f,
                Mathf.Max(0, silenceFadeInMs) * 0.001f,
                easing);
        }

        public enum EasingShape
        {
            /// <summary>線形 (旧挙動)</summary>
            Linear = 0,
            /// <summary>三角関数によるS字 (InOut)。出も入りも滑らか。</summary>
            CosineInOut = 1,
        }
    }
}
