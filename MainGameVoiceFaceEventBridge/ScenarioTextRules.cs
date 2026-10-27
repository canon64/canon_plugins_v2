using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameVoiceFaceEventBridge
{
    [DataContract]
    internal sealed class ScenarioTextRules
    {
        [DataMember] public string ManualTemplate = "{current} {recent}";
        [DataMember] public string AutoTemplate = "{current} {recent}";
        [DataMember] public string CurrentTemplate = "今は{posture}で{action}中。彼女は{reaction}、男側は{male_feel}。動きは{speed_feel}。";
        [DataMember] public string RecentTemplate = "{recent_change}";
        [DataMember] public string MaleFeelTemplate = "{male_feel}";
        [DataMember] public List<ScenarioGaugeRule> ReactionLevelRules = new List<ScenarioGaugeRule>();
        [DataMember] public List<ScenarioGaugeRule> MaleFeelLevelRules = new List<ScenarioGaugeRule>();
        [DataMember] public List<ScenarioSpeedRule> SpeedLevelRules = new List<ScenarioSpeedRule>();

        internal void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ManualTemplate)) ManualTemplate = "{current} {recent}";
            if (string.IsNullOrWhiteSpace(AutoTemplate)) AutoTemplate = "{current} {recent}";
            if (string.IsNullOrWhiteSpace(CurrentTemplate)) CurrentTemplate = "今は{posture}で{action}中。彼女は{reaction}、男側は{male_feel}。動きは{speed_feel}。";
            if (string.IsNullOrWhiteSpace(RecentTemplate)) RecentTemplate = "{recent_change}";
            if (string.IsNullOrWhiteSpace(MaleFeelTemplate)) MaleFeelTemplate = "{male_feel}";

            if (ReactionLevelRules == null || ReactionLevelRules.Count <= 0)
            {
                ReactionLevelRules = CreateDefaultReactionRules();
            }

            if (MaleFeelLevelRules == null || MaleFeelLevelRules.Count <= 0)
            {
                MaleFeelLevelRules = CreateDefaultMaleFeelRules();
            }
            if (SpeedLevelRules == null || SpeedLevelRules.Count <= 0)
            {
                SpeedLevelRules = CreateDefaultSpeedRules();
            }

            for (int i = 0; i < ReactionLevelRules.Count; i++)
            {
                if (ReactionLevelRules[i] == null)
                {
                    ReactionLevelRules[i] = new ScenarioGaugeRule();
                }

                ReactionLevelRules[i].Normalize();
            }

            for (int i = 0; i < MaleFeelLevelRules.Count; i++)
            {
                if (MaleFeelLevelRules[i] == null)
                {
                    MaleFeelLevelRules[i] = new ScenarioGaugeRule();
                }

                MaleFeelLevelRules[i].Normalize();
            }
            for (int i = 0; i < SpeedLevelRules.Count; i++)
            {
                if (SpeedLevelRules[i] == null)
                {
                    SpeedLevelRules[i] = new ScenarioSpeedRule();
                }

                SpeedLevelRules[i].Normalize();
            }
        }

        internal static ScenarioTextRules CreateDefault()
        {
            return new ScenarioTextRules
            {
                ReactionLevelRules = CreateDefaultReactionRules(),
                MaleFeelLevelRules = CreateDefaultMaleFeelRules(),
                SpeedLevelRules = CreateDefaultSpeedRules()
            };
        }

        private static List<ScenarioGaugeRule> CreateDefaultReactionRules()
        {
            return new List<ScenarioGaugeRule>
            {
                new ScenarioGaugeRule { MinGauge = 80f, Label = "かなり感じている" },
                new ScenarioGaugeRule { MinGauge = 50f, Label = "かなり気持ちいい" },
                new ScenarioGaugeRule { MinGauge = 20f, Label = "気持ちよさが出始めている" },
                new ScenarioGaugeRule { MinGauge = 0f, Label = "まだ落ち着いている" }
            };
        }

        private static List<ScenarioGaugeRule> CreateDefaultMaleFeelRules()
        {
            return new List<ScenarioGaugeRule>
            {
                new ScenarioGaugeRule { MinGauge = 80f, Label = "かなり気持ちいい" },
                new ScenarioGaugeRule { MinGauge = 50f, Label = "気持ちよさがかなり高まっている" },
                new ScenarioGaugeRule { MinGauge = 20f, Label = "じわじわ気持ちよくなっている" },
                new ScenarioGaugeRule { MinGauge = 0f, Label = "まだ余裕がある" }
            };
        }

        private static List<ScenarioSpeedRule> CreateDefaultSpeedRules()
        {
            return new List<ScenarioSpeedRule>
            {
                new ScenarioSpeedRule { MinSpeed = 0.8f, Label = "かなり激しい" },
                new ScenarioSpeedRule { MinSpeed = 0.5f, Label = "かなり動いている" },
                new ScenarioSpeedRule { MinSpeed = 0.2f, Label = "ゆっくり" },
                new ScenarioSpeedRule { MinSpeed = 0f, Label = "かなりゆっくり" }
            };
        }
    }

    [DataContract]
    internal sealed class ScenarioGaugeRule
    {
        [DataMember] public float MinGauge;
        [DataMember] public string Label = "落ち着いている";

        internal void Normalize()
        {
            MinGauge = UnityEngine.Mathf.Clamp(MinGauge, 0f, 100f);
            if (string.IsNullOrWhiteSpace(Label))
            {
                Label = "落ち着いている";
            }
        }
    }

    [DataContract]
    internal sealed class ScenarioSpeedRule
    {
        [DataMember] public float MinSpeed;
        [DataMember] public string Label = "かなりゆっくり";

        internal void Normalize()
        {
            MinSpeed = UnityEngine.Mathf.Clamp01(MinSpeed);
            if (string.IsNullOrWhiteSpace(Label))
            {
                Label = "かなりゆっくり";
            }
        }
    }
}
