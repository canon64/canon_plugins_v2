using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using MainGameCharacterMorphBridge;
using MainGameVoiceFaceEventBridge;
using UnityEngine;

namespace MainGameVoiceBreastMorph
{
    [BepInPlugin("com.kks.maingame.voicebreastmorph", "MainGameVoiceBreastMorph", "1.5.0")]
    [BepInDependency("com.kks.maingame.voicefaceeventbridge")]
    [BepInDependency("com.kks.maingame.charactermorphbridge")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _large;
        private ConfigEntry<float> _small;
        private ConfigEntry<float> _seconds;
        private ConfigEntry<int> _femaleIndex;
        private ConfigEntry<string> _tattooFacePath;
        private ConfigEntry<string> _tattooBodyPath;
        private ConfigEntry<string> _tattooKeyword;
        private Coroutine _transition;

        private void Awake()
        {
            _enabled = Config.Bind("Voice breast morph", "00 Enabled", true, "音声テキストによる胸サイズ変更を有効にします。");
            _large = Config.Bind("Voice breast morph", "01 巨乳になるね Target", 1f,
                new ConfigDescription("胸サイズ shapeValueBody[4] の目標値。", new AcceptableValueRange<float>(0f, 1f)));
            _small = Config.Bind("Voice breast morph", "02 ペチャパイになるね Target", 0f,
                new ConfigDescription("胸サイズ shapeValueBody[4] の目標値。", new AcceptableValueRange<float>(0f, 1f)));
            _seconds = Config.Bind("Voice breast morph", "03 Transition Seconds", 10f,
                new ConfigDescription("グラデーションの秒数。モーフブリッジ自身の既定値と同じ10秒。", new AcceptableValueRange<float>(0f, 60f)));
            _femaleIndex = Config.Bind("Voice breast morph", "04 Female Index", -1,
                new ConfigDescription("-1 はモーフブリッジの対象女性設定に従います。", new AcceptableValueRange<int>(-1, 1)));
            SetupAdditionalRules();
            SetupTattooSettings();
            ResponseTextTriggerApi.Received += OnResponseText;
        }

        private void OnDestroy()
        {
            ResponseTextTriggerApi.Received -= OnResponseText;
            if (_transition != null) StopCoroutine(_transition);
            if (_skinTransition != null) StopCoroutine(_skinTransition);
            if (_otherBreastTransition != null) StopCoroutine(_otherBreastTransition);
            CancelTattooFade();
        }

        private void OnResponseText(string text, Func<int, string, Action, float> schedule)
        {
            if (!_enabled.Value || string.IsNullOrEmpty(text)) return;
            int largeIndex = text.IndexOf("巨乳になるね", StringComparison.Ordinal);
            int smallIndex = text.IndexOf("ペチャパイになるね", StringComparison.Ordinal);
            if (largeIndex >= 0 && (smallIndex < 0 || largeIndex <= smallIndex))
                schedule(largeIndex, "breast_large", () => BeginTransition(_large.Value));
            else if (smallIndex >= 0)
                schedule(smallIndex, "breast_small", () => BeginTransition(_small.Value));
            ScheduleAdditionalRules(text, schedule);
            ScheduleTattooTrigger(text, schedule);
        }

        private void BeginTransition(float target)
        {
            if (!_enabled.Value) return;
            if (!TryGetOriginalState(_femaleIndex.Value, out _)) return;
            if (_transition != null) StopCoroutine(_transition);
            if (_otherBreastTransition != null) StopCoroutine(_otherBreastTransition);
            _transition = StartCoroutine(Transition(Mathf.Clamp01(target), _femaleIndex.Value));
        }

        private IEnumerator Transition(float target, int femaleIndex)
        {
            if (!CharacterMorphBridgeApi.TryGetBreast(out float start, femaleIndex))
            {
                Logger.LogWarning("Breast morph skipped: target character unavailable");
                _transition = null;
                yield break;
            }
            float seconds = Mathf.Clamp(_seconds.Value, 0f, 60f);
            if (seconds <= 0f)
            {
                CharacterMorphBridgeApi.SetBreast(target, femaleIndex);
                _transition = null;
                yield break;
            }
            float startedAt = Time.unscaledTime;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed = Mathf.Min(seconds, Time.unscaledTime - startedAt);
                float t = elapsed / seconds;
                // Smoothstep eases both ends while preserving the requested duration.
                float eased = t * t * (3f - 2f * t);
                CharacterMorphBridgeApi.SetBreast(Mathf.Lerp(start, target, eased), femaleIndex);
                yield return new WaitForSecondsRealtime(0.05f);
            }
            CharacterMorphBridgeApi.SetBreast(target, femaleIndex);
            _transition = null;
        }
    }
}
