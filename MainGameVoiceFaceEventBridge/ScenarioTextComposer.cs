using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MainGameVoiceFaceEventBridge
{
    internal static class ScenarioTextComposer
    {
        internal static string Compose(ScenarioStateSnapshot snapshot, PluginSettings settings, ScenarioTextRules rules, string sendMode)
        {
            if (snapshot == null || rules == null)
            {
                return string.Empty;
            }

            if (!snapshot.HasHScene)
            {
                return "現在はHシーン外で、体位やゲージを取得できる場面ではありません。";
            }

            string rootTemplate = string.Equals(sendMode, "auto", StringComparison.OrdinalIgnoreCase)
                ? rules.AutoTemplate
                : rules.ManualTemplate;

            string current = Expand(rules.CurrentTemplate, snapshot, string.Empty, string.Empty, string.Empty);
            string recent = Expand(rules.RecentTemplate, snapshot, current, string.Empty, string.Empty);
            string maleFeel = Expand(rules.MaleFeelTemplate, snapshot, current, recent, string.Empty);
            string text = Expand(rootTemplate, snapshot, current, recent, maleFeel);
            return CollapseSpaces(text);
        }

        private static string Expand(
            string template,
            ScenarioStateSnapshot snapshot,
            string current,
            string recent,
            string maleFeel)
        {
            string text = template ?? string.Empty;
            text = text.Replace("{current}", current ?? string.Empty);
            text = text.Replace("{recent}", recent ?? string.Empty);
            text = text.Replace("{mood}", maleFeel ?? string.Empty);
            text = text.Replace("{male_feel}", Safe(snapshot.MaleFeelLabel, "まだ余裕がある"));
            text = text.Replace("{speed_feel}", Safe(snapshot.SpeedFeelLabel, "かなりゆっくり"));
            text = text.Replace("{posture}", Safe(snapshot.PostureName, "不明な体位"));
            text = text.Replace("{action}", Safe(snapshot.ActionLabel, "Hシーン"));
            text = text.Replace("{reaction}", Safe(snapshot.ReactionLabel, "通常"));
            text = text.Replace("{recent_change}", Safe(snapshot.RecentChangeText, "同じ流れが続いている。"));
            text = text.Replace("{current_song}", Safe(snapshot.CurrentSongLabel, "曲は未取得"));
            text = text.Replace("{song}", Safe(snapshot.CurrentSongLabel, "曲は未取得"));
            text = text.Replace("{kiss_state}", Safe(snapshot.KissLabel, "キス状態は未取得"));
            text = text.Replace("{arousal}", Safe(snapshot.ArousalLabel, "ゲージ未取得"));
            text = text.Replace("{sensitivity}", Safe(snapshot.SensitivityLabel, "感度は未取得"));
            text = text.Replace("{mode}", Safe(snapshot.ModeName, "none"));
            text = text.Replace("{mode_value}", snapshot.ModeValue.ToString(CultureInfo.InvariantCulture));
            text = text.Replace("{speed}", snapshot.SpeedCalc.ToString("0.##", CultureInfo.InvariantCulture));
            text = text.Replace("{female_gauge}", snapshot.FemaleGauge.ToString("0", CultureInfo.InvariantCulture));
            text = text.Replace("{male_gauge}", snapshot.MaleGauge.ToString("0", CultureInfo.InvariantCulture));
            return text;
        }

        private static string CollapseSpaces(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return Regex.Replace(text.Trim(), @"[ \t　]+", " ");
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
