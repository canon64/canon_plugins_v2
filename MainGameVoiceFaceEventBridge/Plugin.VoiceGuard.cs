using System;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.VoiceGuard.cs
    //
    // 責務: 外部音声再生中にゲーム本体のボイス/キスイベントをブロックするか
    //       否かを判定する public API (VoiceGuardHooks から呼ばれる)。
    //       ブロック判定とブロック検知時のログ/状態書き込みだけを置く。
    internal sealed partial class Plugin
    {
        internal bool ShouldBlockGameVoiceEvents()
        {
            if (_externalVoicePlayer != null && _externalVoicePlayer.PriorityAibuCueActive) return true;
            if (_externalVoicePlayer != null && _externalVoicePlayer.IsThoughtPlaying) return false;
            // Cue ownership is mandatory even if the optional dialogue guard is disabled.
            if (_externalVoicePlayer != null && _externalVoicePlayer.ImpactCueBusy
                && CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return true;
            var s = Settings;
            if (s == null || !s.Enabled || !s.BlockGameVoiceWhileExternalPlaying)
            {
                return false;
            }

            if (_externalVoicePlayer != null && _externalVoicePlayer.IsPlaying)
            {
                return true;
            }

            return _voiceGuardState.IsWithinBlock(Time.unscaledTime);
        }

        internal bool ShouldBlockKissActions()
        {
            if (_externalVoicePlayer != null && _externalVoicePlayer.IsThoughtPlaying) return false;
            var s = Settings;
            if (s == null || !s.Enabled)
            {
                return false;
            }

            if (_externalVoicePlayer != null && _externalVoicePlayer.IsPlaying)
            {
                return true;
            }

            return _voiceGuardState.IsWithinBlock(Time.unscaledTime);
        }

        internal void OnGameVoiceEventBlocked(string point)
        {
        }

        internal void OnKissActionBlocked(string point)
        {
        }
    }
}
