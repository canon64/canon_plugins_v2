using MainGameCharacterMorphBridge;
using System;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private void ScheduleMorphBridgeTriggersFromText(string text, Func<int, string, Action, float> scheduleAction)
        {
            PluginSettings settings = Settings;
            if (settings == null || !settings.EnableMorphBridgeTriggers || string.IsNullOrWhiteSpace(text) || scheduleAction == null)
                return;

            string[] resetKeywords = SplitKeywords((settings.MorphBridgeResetKeywords ?? "元に戻るね").Replace('、', ','));
            int resetIndex = FindFirstKeywordIndex(text, resetKeywords, StringComparison.Ordinal);
            float seconds = ResolveMorphBridgeTransitionSeconds();

            if (resetIndex >= 0)
            {
                float delay = scheduleAction(resetIndex, "morph_reset", () => ExecuteMorphBridgeReset(seconds));
                Log("[morph] reset trigger matched pos=" + resetIndex + " delay=" + delay.ToString("F2") + " seconds=" + seconds.ToString("F2"));
                return;
            }

            // 変身は合言葉ごとのカード一覧だけを見る。
            // 以前は汎用キーワード(変身するね)からMorphBridge側の一枚を呼ぶ経路もあったが、
            // 系統が二つあると「どちらに登録したか」で挙動が変わって追えなくなるので一本化した。
            int cardIndex;
            MorphCardEntry matchedCard = FindMorphCardInText(text, out cardIndex);
            if (matchedCard != null)
            {
                string cardPathToUse = matchedCard.CardPath;
                float cardDelay = scheduleAction(cardIndex, "morph_card", () => ExecuteMorphBridgeTransformByPath(cardPathToUse, seconds));
                Log("[morph] card trigger matched pos=" + cardIndex
                    + " delay=" + cardDelay.ToString("F2")
                    + " seconds=" + seconds.ToString("F2")
                    + " keyword=" + matchedCard.Keyword
                    + " path=" + cardPathToUse);
                return;
            }

            Log("[morph] no trigger matched");
        }

        /// <summary>合言葉から引いたカードのパスで直接変身する。カード登録を経由しない。</summary>
        private void ExecuteMorphBridgeTransformByPath(string cardPath, float seconds)
        {
            if (!CharacterMorphBridgeApi.IsAvailable)
            {
                LogWarn("[morph] card transform failed result=bridge_unavailable");
                return;
            }

            bool ok = CharacterMorphBridgeApi.BlendToCardPath(cardPath, 1f, seconds);
            if (ok)
                Log("[morph] card transform requested result=ok seconds=" + seconds.ToString("F2") + " path=" + cardPath);
            else
                LogWarn("[morph] card transform failed result=api_false path=" + cardPath);
        }

        private void ExecuteMorphBridgeReset(float seconds)
        {
            if (!CharacterMorphBridgeApi.IsAvailable)
            {
                LogWarn("[morph] reset failed result=bridge_unavailable");
                return;
            }

            bool ok = CharacterMorphBridgeApi.BlendTo(0f, seconds);
            if (ok)
            {
                Log("[morph] reset requested result=ok seconds=" + seconds.ToString("F2"));
            }
            else
            {
                LogWarn("[morph] reset failed result=api_false seconds=" + seconds.ToString("F2"));
            }
        }

        private float ResolveMorphBridgeTransitionSeconds()
        {
            float seconds = _cfgMorphBridgeTransitionSeconds != null
                ? _cfgMorphBridgeTransitionSeconds.Value
                : (Settings != null ? Settings.MorphBridgeTransitionSeconds : 5f);
            return Mathf.Clamp(seconds, 0f, 60f);
        }
    }
}
