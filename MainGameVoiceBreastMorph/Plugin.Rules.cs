using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using MainGameCharacterMorphBridge;
using UnityEngine;

namespace MainGameVoiceBreastMorph
{
    public sealed partial class Plugin
    {
        // Stored in one config entry so additions and deletions persist without requiring a restart.
        private const string DefaultAdditionalRules =
            "色黒になるね|Skin|#754B3A|10|1;白肌になるね|Skin|#F4E0D5|10|0;" +
            "元のおっぱいに戻るね|BreastOriginal|-|10|-1;元の肌色に戻るね|SkinOriginal|-|10|-1";

        private sealed class OriginalState
        {
            internal float Breast;
            internal Color SkinMain, SkinSub;
            internal int SkinId;
        }

        private readonly Dictionary<int, OriginalState> _originalStates = new Dictionary<int, OriginalState>();

        private sealed class Rule
        {
            internal string Keyword, Kind, Value;
            internal float Seconds;
            internal int SkinId;
        }

        private ConfigEntry<string> _rules;
        private ConfigEntry<bool> _ruleList;
        private ConfigEntry<string> _ruleKeyword, _ruleKind, _ruleValue;
        private ConfigEntry<float> _ruleSeconds;
        private ConfigEntry<int> _ruleSkinId;
        private ConfigEntry<bool> _ruleAddButton;
        private ConfigEntry<bool> _resetRulesMigrated;
        private Coroutine _skinTransition;
        private Coroutine _otherBreastTransition;
        private string _ruleMessage = "";

        private void SetupAdditionalRules()
        {
            const string section = "Addable triggers";
            _rules = Config.Bind(section, "00 Saved Rules", DefaultAdditionalRules,
                new ConfigDescription("合言葉|種類|値|秒数|skinId を ; で区切った保存値。", null,
                    new ConfigurationManager.ConfigurationManagerAttributes { Browsable = false }));
            _resetRulesMigrated = Config.Bind(section, "00 Reset Rules Added", false,
                new ConfigDescription("既存設定への戻す合言葉の追加済みフラグ。", null,
                    new ConfigurationManager.ConfigurationManagerAttributes { Browsable = false }));
            if (!_resetRulesMigrated.Value)
            {
                var existing = ReadRules();
                foreach (string keyword in new[] { "元のおっぱいに戻るね", "元の肌色に戻るね" })
                {
                    if (existing.All(r => r.Keyword != keyword))
                        existing.Add(new Rule { Keyword = keyword, Kind = keyword.Contains("おっぱい") ? "BreastOriginal" : "SkinOriginal",
                            Value = "-", Seconds = 10f, SkinId = -1 });
                }
                SaveRules(existing);
                _resetRulesMigrated.Value = true;
            }
            _ruleList = Config.Bind(section, "01 List", false, Drawer(DrawRuleList, 100));
            _ruleKeyword = Config.Bind(section, "02 Keyword", "", Field("合言葉を入力。", 90));
            _ruleKind = Config.Bind(section, "03 Kind", "Skin", Field("Skin / Breast / SkinOriginal / BreastOriginal。", 80));
            _ruleValue = Config.Bind(section, "04 Value", "#754B3A", Field("Skin は #RRGGBB、Breast は 0～1。Original は -。", 70));
            _ruleSeconds = Config.Bind(section, "05 Seconds", 10f,
                new ConfigDescription("この合言葉の変化秒数。初期値はモーフブリッジと同じ10秒。",
                    new AcceptableValueRange<float>(0f, 60f), new ConfigurationManager.ConfigurationManagerAttributes { Order = 60 }));
            _ruleSkinId = Config.Bind(section, "06 Skin ID", -1,
                new ConfigDescription("Skin の種類。-1 は現在の種類を維持。色黒は1、白肌は0を初期登録。",
                    new AcceptableValueRange<int>(-1, 255), new ConfigurationManager.ConfigurationManagerAttributes { Order = 50 }));
            _ruleAddButton = Config.Bind(section, "07 Add or update", false, Drawer(_ =>
            {
                if (GUILayout.Button("追加 / 更新", GUILayout.ExpandWidth(true))) UpsertRule();
            }, 40));
        }

        private static ConfigDescription Field(string text, int order) =>
            new ConfigDescription(text, null, new ConfigurationManager.ConfigurationManagerAttributes { Order = order });

        private static ConfigDescription Drawer(Action<ConfigEntryBase> drawer, int order) =>
            new ConfigDescription("", null, new ConfigurationManager.ConfigurationManagerAttributes
            { Order = order, HideSettingName = true, HideDefaultButton = true, CustomDrawer = drawer });

        private List<Rule> ReadRules()
        {
            var result = new List<Rule>();
            foreach (string line in (_rules.Value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|');
                if (parts.Length != 5 || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
                    || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int skinId)) continue;
                var rule = new Rule { Keyword = parts[0].Trim(), Kind = parts[1].Trim(), Value = parts[2].Trim(),
                    Seconds = Mathf.Clamp(seconds, 0f, 60f), SkinId = skinId };
                if (Valid(rule)) result.Add(rule);
            }
            return result;
        }

        private static bool Valid(Rule r)
        {
            if (string.IsNullOrWhiteSpace(r.Keyword) || r.Keyword.IndexOfAny(new[] { '|', ';' }) >= 0) return false;
            if (r.Kind.Equals("Breast", StringComparison.OrdinalIgnoreCase))
                return float.TryParse(r.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0f && value <= 1f;
            if (r.Kind.Equals("Skin", StringComparison.OrdinalIgnoreCase))
                return ColorUtility.TryParseHtmlString(r.Value, out _) && r.SkinId >= -1 && r.SkinId <= 255;
            if (r.Kind.Equals("BreastOriginal", StringComparison.OrdinalIgnoreCase)
                || r.Kind.Equals("SkinOriginal", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void SaveRules(List<Rule> rules)
        {
            _rules.Value = string.Join(";", rules.Select(r => string.Join("|", new[] { r.Keyword, r.Kind, r.Value,
                r.Seconds.ToString("0.##", CultureInfo.InvariantCulture), r.SkinId.ToString(CultureInfo.InvariantCulture) })).ToArray());
            Config.Save();
        }

        private void DrawRuleList(ConfigEntryBase _)
        {
            GUILayout.BeginVertical();
            foreach (Rule rule in ReadRules())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(rule.Keyword + " → " + rule.Kind + " " + rule.Value + " / " + rule.Seconds.ToString("0.##") + "秒", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("編集", GUILayout.Width(55f)))
                {
                    _ruleKeyword.Value = rule.Keyword; _ruleKind.Value = rule.Kind; _ruleValue.Value = rule.Value;
                    _ruleSeconds.Value = rule.Seconds; _ruleSkinId.Value = rule.SkinId;
                }
                if (GUILayout.Button("削除", GUILayout.Width(55f)))
                {
                    var rules = ReadRules(); rules.RemoveAll(r => r.Keyword == rule.Keyword); SaveRules(rules);
                    _ruleMessage = "削除: " + rule.Keyword;
                }
                GUILayout.EndHorizontal();
            }
            if (_ruleMessage.Length > 0) GUILayout.Label(_ruleMessage);
            GUILayout.EndVertical();
        }

        private void UpsertRule()
        {
            var rule = new Rule { Keyword = (_ruleKeyword.Value ?? "").Trim(), Kind = (_ruleKind.Value ?? "").Trim(),
                Value = (_ruleValue.Value ?? "").Trim(), Seconds = _ruleSeconds.Value, SkinId = _ruleSkinId.Value };
            if (!Valid(rule)) { _ruleMessage = "入力が不正です。Skin は #RRGGBB、Breast は0～1。"; return; }
            var rules = ReadRules();
            int index = rules.FindIndex(r => r.Keyword == rule.Keyword);
            if (index >= 0) rules[index] = rule; else rules.Add(rule);
            SaveRules(rules);
            _ruleMessage = (index >= 0 ? "更新: " : "追加: ") + rule.Keyword;
        }

        private void ScheduleAdditionalRules(string text, Func<int, string, Action, float> schedule)
        {
            foreach (Rule rule in ReadRules())
            {
                int search = 0;
                while (search < text.Length)
                {
                    int index = text.IndexOf(rule.Keyword, search, StringComparison.Ordinal);
                    if (index < 0) break;
                    Rule selected = rule;
                    schedule(index, "body_morph", () => BeginRule(selected));
                    search = index + rule.Keyword.Length;
                }
            }
        }

        private void BeginRule(Rule rule)
        {
            if (!_enabled.Value) return;
            int femaleIndex = _femaleIndex.Value;
            if (!TryGetOriginalState(femaleIndex, out OriginalState original)) return;
            if (rule.Kind.Equals("Breast", StringComparison.OrdinalIgnoreCase)
                || rule.Kind.Equals("BreastOriginal", StringComparison.OrdinalIgnoreCase))
            {
                if (_otherBreastTransition != null) StopCoroutine(_otherBreastTransition);
                if (_transition != null) StopCoroutine(_transition);
                float target = rule.Kind.Equals("BreastOriginal", StringComparison.OrdinalIgnoreCase)
                    ? original.Breast : float.Parse(rule.Value, CultureInfo.InvariantCulture);
                _otherBreastTransition = StartCoroutine(TransitionRuleBreast(target, rule.Seconds, femaleIndex));
            }
            else
            {
                if (_skinTransition != null) StopCoroutine(_skinTransition);
                bool reset = rule.Kind.Equals("SkinOriginal", StringComparison.OrdinalIgnoreCase);
                Color color = original.SkinMain;
                if (!reset) ColorUtility.TryParseHtmlString(rule.Value, out color);
                _skinTransition = StartCoroutine(TransitionSkin(color, reset ? original.SkinSub : color,
                    reset ? original.SkinId : rule.SkinId, rule.Seconds, femaleIndex));
            }
        }

        private bool TryGetOriginalState(int femaleIndex, out OriginalState original)
        {
            original = null;
            if (!CharacterMorphBridgeApi.TryGetTargetIdentity(out int identity, femaleIndex)) return false;
            if (_originalStates.TryGetValue(identity, out original)) return true;
            if (!CharacterMorphBridgeApi.TryGetBreast(out float breast, femaleIndex)
                || !CharacterMorphBridgeApi.TryGetSkin(out Color main, out Color sub, out int skinId, femaleIndex)) return false;
            original = new OriginalState { Breast = breast, SkinMain = main, SkinSub = sub, SkinId = skinId };
            _originalStates.Add(identity, original);
            Logger.LogInfo("Original breast and skin captured for character " + identity);
            return true;
        }

        private IEnumerator TransitionRuleBreast(float target, float seconds, int femaleIndex)
        {
            if (!CharacterMorphBridgeApi.TryGetBreast(out float start, femaleIndex))
            { Logger.LogWarning("Breast morph skipped: target character unavailable"); _otherBreastTransition = null; yield break; }
            float startedAt = Time.unscaledTime;
            float elapsed = 0f;
            do
            {
                elapsed = seconds <= 0f ? seconds : Mathf.Min(seconds, Time.unscaledTime - startedAt);
                float t = seconds <= 0f ? 1f : elapsed / seconds;
                CharacterMorphBridgeApi.SetBreast(Mathf.Lerp(start, target, t * t * (3f - 2f * t)), femaleIndex);
                if (elapsed >= seconds) break;
                yield return new WaitForSecondsRealtime(0.05f);
            } while (true);
            _otherBreastTransition = null;
        }

        private IEnumerator TransitionSkin(Color targetMain, Color targetSub, int targetSkinId, float seconds, int femaleIndex)
        {
            if (!CharacterMorphBridgeApi.TryGetSkin(out Color startMain, out Color startSub, out int startSkinId, femaleIndex))
            { Logger.LogWarning("Skin morph skipped: target character unavailable"); _skinTransition = null; yield break; }
            float startedAt = Time.unscaledTime;
            float elapsed = 0f;
            do
            {
                elapsed = seconds <= 0f ? seconds : Mathf.Min(seconds, Time.unscaledTime - startedAt);
                float t = seconds <= 0f ? 1f : elapsed / seconds;
                int skinId = targetSkinId < 0 || t < 0.5f ? startSkinId : targetSkinId;
                float eased = t * t * (3f - 2f * t);
                CharacterMorphBridgeApi.SetSkin(Color.Lerp(startMain, targetMain, eased),
                    Color.Lerp(startSub, targetSub, eased), skinId, femaleIndex);
                if (elapsed >= seconds) break;
                yield return new WaitForSecondsRealtime(0.25f);
            } while (true);
            _skinTransition = null;
        }
    }
}
