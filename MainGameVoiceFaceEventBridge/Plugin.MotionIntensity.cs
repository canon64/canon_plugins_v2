using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private ConfigEntry<string> _cfgMotionTargetKeywords;
        private readonly ConfigEntry<string>[] _cfgMotionLevelKeywords = new ConfigEntry<string>[5];
        private static MethodInfo _composerSetMotionLevel;

        private void InitializeComposerMotionIntensityConfig()
        {
            const string section = "ComposerMotionIntensity";
            _cfgMotionTargetKeywords = Config.Bind(section, "TargetKeywords", "腰の動き,腰を,腰が",
                "Composerの動き変更を発動する対象語。カンマ区切り。同じ文に段階語も必要。");
            string[] defaults =
            {
                "とてもおとなしく,すごくおとなしく,とても大人しく",
                "おとなしく,大人しく,ゆっくり",
                "普通に,いつも通り,普段通り",
                "激しく,強く動かす",
                "すごく激しく,とても激しく,もっと激しく"
            };
            string[] names = { "VeryQuiet", "Quiet", "Normal", "Intense", "VeryIntense" };
            for (int i = 0; i < names.Length; i++)
                _cfgMotionLevelKeywords[i] = Config.Bind(section, names[i] + "Keywords", defaults[i],
                    "Composerへ送る動きの段階語。カンマ区切り。対象語と同じ文にあれば発動。");
        }

        private void ScheduleComposerMotionIntensityFromText(string text, Func<int, string, Action, float> schedule)
        {
            if (schedule == null) return;
            string[] targets = SplitKeywords(_cfgMotionTargetKeywords != null ? _cfgMotionTargetKeywords.Value : string.Empty);
            var levels = new string[_cfgMotionLevelKeywords.Length][];
            for (int i = 0; i < levels.Length; i++)
                levels[i] = SplitKeywords(_cfgMotionLevelKeywords[i] != null ? _cfgMotionLevelKeywords[i].Value : string.Empty);
            foreach (MotionIntensityHit hit in MotionIntensityTextMatcher.FindHits(text, targets, levels))
            {
                int level = hit.Level;
                int index = hit.Index;
                schedule(index, "composer-motion-intensity", () =>
                {
                    if (TryForwardComposerMotionLevel(level)) Log("[composer-motion] level=" + level);
                    else LogWarn("[composer-motion] Composer unavailable; level=" + level);
                });
            }
        }

        private static bool TryForwardComposerMotionLevel(int level)
        {
            try
            {
                if (_composerSetMotionLevel == null)
                {
                    Type composer = Type.GetType("MainGameObjectComposer.Plugin, MainGameObjectComposer", false)
                        ?? HarmonyLib.AccessTools.TypeByName("MainGameObjectComposer.Plugin");
                    if (composer == null) return false;
                    _composerSetMotionLevel = composer.GetMethod("TrySetMotionIntensityLevel",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                }
                return _composerSetMotionLevel != null && (bool)_composerSetMotionLevel.Invoke(null, new object[] { level });
            }
            catch { return false; }
        }
    }
}
