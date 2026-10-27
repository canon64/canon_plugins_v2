using BepInEx.Configuration;
using ConfigurationManager;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private ConfigEntry<bool> _cfgScenarioTextEnabled;
        private ConfigEntry<bool> _cfgScenarioManualSendRequested;
        private ConfigEntry<bool> _cfgScenarioAutoSendEnabled;
        private ConfigEntry<float> _cfgScenarioAutoSendIntervalSeconds;
        private ConfigEntry<string> _cfgScenarioManualTemplate;
        private ConfigEntry<string> _cfgScenarioAutoTemplate;
        private ConfigEntry<string> _cfgScenarioCurrentTemplate;
        private ConfigEntry<string> _cfgScenarioRecentTemplate;
        private ConfigEntry<string> _cfgScenarioMaleFeelTemplate;
        private ConfigEntry<bool> _cfgScenarioReactionRulesExpanded;
        private ConfigEntry<bool> _cfgScenarioMaleFeelRulesExpanded;
        private ConfigEntry<bool> _cfgScenarioSpeedRulesExpanded;

        private ScenarioStateSnapshot _lastScenarioSnapshot;
        private ScenarioTextRules _scenarioTextRules;
        private float _nextScenarioAutoSendTime;
        private float _nextScenarioSkipLogTime;
        private bool _scenarioReactionRulesExpanded;
        private bool _scenarioMaleFeelRulesExpanded;
        private bool _scenarioSpeedRulesExpanded;
        private bool _suppressScenarioRulesConfigChangeEvent;
        private readonly Dictionary<ConfigEntryBase, ConfigurationManagerAttributes> _cfgScenarioRulesAttributes =
            new Dictionary<ConfigEntryBase, ConfigurationManagerAttributes>();
        private readonly List<ScenarioReactionRuleConfigEntrySet> _cfgScenarioReactionRuleEntries =
            new List<ScenarioReactionRuleConfigEntrySet>();
        private readonly List<ScenarioGaugeRuleConfigEntrySet> _cfgScenarioMaleFeelRuleEntries =
            new List<ScenarioGaugeRuleConfigEntrySet>();
        private readonly List<ScenarioSpeedRuleConfigEntrySet> _cfgScenarioSpeedRuleEntries =
            new List<ScenarioSpeedRuleConfigEntrySet>();

        private const string ScenarioReactionRulesSectionName = "ScenarioTextReactionRules";
        private const string ScenarioMaleFeelRulesSectionName = "ScenarioTextMaleFeelRules";
        private const string ScenarioSpeedRulesSectionName = "ScenarioTextSpeedRules";

        private sealed class ScenarioReactionRuleConfigEntrySet
        {
            public int Index;
            public ConfigEntry<float> MinFemaleGaugeEntry;
            public ConfigEntry<string> LabelEntry;
        }

        private sealed class ScenarioGaugeRuleConfigEntrySet
        {
            public int Index;
            public ConfigEntry<float> MinGaugeEntry;
            public ConfigEntry<string> LabelEntry;
        }

        private sealed class ScenarioSpeedRuleConfigEntrySet
        {
            public int Index;
            public ConfigEntry<float> MinSpeedEntry;
            public ConfigEntry<string> LabelEntry;
        }

        private void LoadScenarioTextRules()
        {
            _scenarioTextRules = ScenarioTextRulesStore.LoadOrCreate(PluginDir, Log, LogWarn, LogError);
            EnsureScenarioTextRuleConfigEntries();
            SyncScenarioTextRuleConfigEntriesFromRuntime();
            UpdateScenarioTextRuleConfigEntryState();
        }

        private void BindScenarioTextConfigEntries()
        {
            const string section = "ScenarioText";
            _cfgScenarioTextEnabled = Config.Bind(
                section,
                "Enabled",
                false,
                "現在状況テキスト送信を有効化する。");
            _cfgScenarioManualSendRequested = Config.Bind(
                section,
                "ManualSendRequested",
                false,
                "ONにすると現在状況テキストを1回送信し、自動でOFFへ戻す。");
            _cfgScenarioAutoSendEnabled = Config.Bind(
                section,
                "AutoSendEnabled",
                false,
                "現在状況テキストを定期送信する。");
            _cfgScenarioAutoSendIntervalSeconds = Config.Bind(
                section,
                "AutoSendIntervalSeconds",
                60.0f,
                new ConfigDescription(
                    "定期送信間隔（秒）。",
                    new AcceptableValueRange<float>(2f, 300f)));
            _cfgScenarioManualTemplate = Config.Bind(
                section,
                "ManualTemplate",
                "{current} {recent}",
                "手動送信用の文面テンプレート。");
            _cfgScenarioAutoTemplate = Config.Bind(
                section,
                "AutoTemplate",
                "{current} {recent}",
                "定期送信用の文面テンプレート。");
            _cfgScenarioCurrentTemplate = Config.Bind(
                section,
                "CurrentTemplate",
                "今は{posture}で{action}中。彼女は{reaction}、男側は{male_feel}。動きは{speed_feel}。",
                "現在状況部分のテンプレート。");
            _cfgScenarioRecentTemplate = Config.Bind(
                section,
                "RecentTemplate",
                "{recent_change}",
                "直近変化部分のテンプレート。");
            _cfgScenarioMaleFeelTemplate = Config.Bind(
                section,
                "MaleFeelTemplate",
                "{male_feel}",
                "男側の気持ちよさ部分のテンプレート。");

            EnsureScenarioTextRuleConfigEntries();
        }

        private void RegisterScenarioTextConfigEntryEvents()
        {
            HookConfigEntryEvent(_cfgScenarioTextEnabled, restartPipe: false);
            HookConfigEntryEvent(_cfgScenarioManualSendRequested, restartPipe: false);
            HookConfigEntryEvent(_cfgScenarioAutoSendEnabled, restartPipe: false);
            HookConfigEntryEvent(_cfgScenarioAutoSendIntervalSeconds, restartPipe: false);
            RegisterScenarioTemplateConfigEntryEvents();
        }

        private void RegisterScenarioTemplateConfigEntryEvents()
        {
            HookScenarioTemplateConfigEntry(_cfgScenarioManualTemplate, "scenario-manual-template");
            HookScenarioTemplateConfigEntry(_cfgScenarioAutoTemplate, "scenario-auto-template");
            HookScenarioTemplateConfigEntry(_cfgScenarioCurrentTemplate, "scenario-current-template");
            HookScenarioTemplateConfigEntry(_cfgScenarioRecentTemplate, "scenario-recent-template");
            HookScenarioTemplateConfigEntry(_cfgScenarioMaleFeelTemplate, "scenario-male-feel-template");
        }

        private void HookScenarioTemplateConfigEntry(ConfigEntry<string> entry, string reason)
        {
            HookManagedConfigEntry(
                entry,
                reason,
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ =>
                {
                    ApplyScenarioTemplateConfigEntries();
                    return true;
                },
                currentReason => SaveScenarioTextRules("config-manager:" + currentReason));
        }

        private void ApplyScenarioTemplateConfigEntries()
        {
            if (_scenarioTextRules == null)
            {
                return;
            }

            _scenarioTextRules.ManualTemplate = _cfgScenarioManualTemplate != null ? _cfgScenarioManualTemplate.Value : _scenarioTextRules.ManualTemplate;
            _scenarioTextRules.AutoTemplate = _cfgScenarioAutoTemplate != null ? _cfgScenarioAutoTemplate.Value : _scenarioTextRules.AutoTemplate;
            _scenarioTextRules.CurrentTemplate = _cfgScenarioCurrentTemplate != null ? _cfgScenarioCurrentTemplate.Value : _scenarioTextRules.CurrentTemplate;
            _scenarioTextRules.RecentTemplate = _cfgScenarioRecentTemplate != null ? _cfgScenarioRecentTemplate.Value : _scenarioTextRules.RecentTemplate;
            _scenarioTextRules.MaleFeelTemplate = _cfgScenarioMaleFeelTemplate != null ? _cfgScenarioMaleFeelTemplate.Value : _scenarioTextRules.MaleFeelTemplate;
            _scenarioTextRules.Normalize();
        }

        private void EnsureScenarioTextRuleConfigEntries()
        {
            EnsureScenarioReactionRuleSectionControlEntry();
            EnsureScenarioMaleFeelRuleSectionControlEntry();
            EnsureScenarioSpeedRuleSectionControlEntry();
            EnsureScenarioReactionRuleEntries();
            EnsureScenarioMaleFeelRuleEntries();
            EnsureScenarioSpeedRuleEntries();
            SyncScenarioTextRuleConfigEntriesFromRuntime();
            UpdateScenarioTextRuleConfigEntryState();
        }

        private void EnsureScenarioReactionRuleSectionControlEntry()
        {
            if (_cfgScenarioReactionRulesExpanded == null)
            {
                _cfgScenarioReactionRulesExpanded = Config.Bind(
                    ScenarioReactionRulesSectionName,
                    "【表示】Reactionルール一覧",
                    _scenarioReactionRulesExpanded,
                    BuildScenarioTextToggleButtonDescription(
                        openLabel: "Reactionルール一覧を開く",
                        closeLabel: "Reactionルール一覧を閉じる",
                        order: 1000,
                        readOnly: false));
                RegisterScenarioRuleAttribute(_cfgScenarioReactionRulesExpanded);
                HookManagedConfigEntry(
                    _cfgScenarioReactionRulesExpanded,
                    "scenario-reaction-expand-toggle",
                    () => _suppressScenarioRulesConfigChangeEvent,
                    value =>
                    {
                        _scenarioReactionRulesExpanded = value;
                        UpdateScenarioTextRuleConfigEntryState();
                        return true;
                    },
                    saveValue: null,
                    refreshConfigManager: true);
            }

            _scenarioReactionRulesExpanded = _cfgScenarioReactionRulesExpanded != null && _cfgScenarioReactionRulesExpanded.Value;
        }

        private void EnsureScenarioMaleFeelRuleSectionControlEntry()
        {
            if (_cfgScenarioMaleFeelRulesExpanded == null)
            {
                _cfgScenarioMaleFeelRulesExpanded = Config.Bind(
                    ScenarioMaleFeelRulesSectionName,
                    "【表示】MaleFeelルール一覧",
                    _scenarioMaleFeelRulesExpanded,
                    BuildScenarioTextToggleButtonDescription(
                        openLabel: "MaleFeelルール一覧を開く",
                        closeLabel: "MaleFeelルール一覧を閉じる",
                        order: 1000,
                        readOnly: false));
                RegisterScenarioRuleAttribute(_cfgScenarioMaleFeelRulesExpanded);
                HookManagedConfigEntry(
                    _cfgScenarioMaleFeelRulesExpanded,
                    "scenario-male-feel-expand-toggle",
                    () => _suppressScenarioRulesConfigChangeEvent,
                    value =>
                    {
                        _scenarioMaleFeelRulesExpanded = value;
                        UpdateScenarioTextRuleConfigEntryState();
                        return true;
                    },
                    saveValue: null,
                    refreshConfigManager: true);
            }

            _scenarioMaleFeelRulesExpanded = _cfgScenarioMaleFeelRulesExpanded != null && _cfgScenarioMaleFeelRulesExpanded.Value;
        }

        private void EnsureScenarioSpeedRuleSectionControlEntry()
        {
            if (_cfgScenarioSpeedRulesExpanded == null)
            {
                _cfgScenarioSpeedRulesExpanded = Config.Bind(
                    ScenarioSpeedRulesSectionName,
                    "【表示】Speedルール一覧",
                    _scenarioSpeedRulesExpanded,
                    BuildScenarioTextToggleButtonDescription(
                        openLabel: "Speedルール一覧を開く",
                        closeLabel: "Speedルール一覧を閉じる",
                        order: 1000,
                        readOnly: false));
                RegisterScenarioRuleAttribute(_cfgScenarioSpeedRulesExpanded);
                HookManagedConfigEntry(
                    _cfgScenarioSpeedRulesExpanded,
                    "scenario-speed-expand-toggle",
                    () => _suppressScenarioRulesConfigChangeEvent,
                    value =>
                    {
                        _scenarioSpeedRulesExpanded = value;
                        UpdateScenarioTextRuleConfigEntryState();
                        return true;
                    },
                    saveValue: null,
                    refreshConfigManager: true);
            }

            _scenarioSpeedRulesExpanded = _cfgScenarioSpeedRulesExpanded != null && _cfgScenarioSpeedRulesExpanded.Value;
        }

        private void EnsureScenarioReactionRuleEntries()
        {
            List<ScenarioGaugeRule> rules = _scenarioTextRules != null ? _scenarioTextRules.ReactionLevelRules : null;
            if (rules == null)
            {
                return;
            }

            int order = 900;
            while (_cfgScenarioReactionRuleEntries.Count < rules.Count)
            {
                int index = _cfgScenarioReactionRuleEntries.Count;
                string prefix = "Rule" + (index + 1).ToString(CultureInfo.InvariantCulture);
                var set = new ScenarioReactionRuleConfigEntrySet
                {
                    Index = index,
                    LabelEntry = Config.Bind(
                        ScenarioReactionRulesSectionName,
                        prefix + "_Label",
                        string.Empty,
                        BuildScenarioTextRuleConfigDescription("Reaction文言", order--, readOnly: false, browsable: _scenarioReactionRulesExpanded)),
                    MinFemaleGaugeEntry = Config.Bind(
                        ScenarioReactionRulesSectionName,
                        prefix + "_MinGauge",
                        0f,
                        BuildScenarioTextRuleConfigDescription("女の子感度ゲージしきい値", order--, readOnly: false, browsable: _scenarioReactionRulesExpanded))
                };
                RegisterScenarioRuleAttribute(set.LabelEntry);
                RegisterScenarioRuleAttribute(set.MinFemaleGaugeEntry);
                AttachScenarioReactionRuleHandlers(set);
                _cfgScenarioReactionRuleEntries.Add(set);
            }
        }

        private void EnsureScenarioMaleFeelRuleEntries()
        {
            List<ScenarioGaugeRule> rules = _scenarioTextRules != null ? _scenarioTextRules.MaleFeelLevelRules : null;
            if (rules == null)
            {
                return;
            }

            int order = 900;
            while (_cfgScenarioMaleFeelRuleEntries.Count < rules.Count)
            {
                int index = _cfgScenarioMaleFeelRuleEntries.Count;
                string prefix = "Rule" + (index + 1).ToString(CultureInfo.InvariantCulture);
                var set = new ScenarioGaugeRuleConfigEntrySet
                {
                    Index = index,
                    LabelEntry = Config.Bind(
                        ScenarioMaleFeelRulesSectionName,
                        prefix + "_Label",
                        string.Empty,
                        BuildScenarioTextRuleConfigDescription("MaleFeel文言", order--, readOnly: false, browsable: _scenarioMaleFeelRulesExpanded)),
                    MinGaugeEntry = Config.Bind(
                        ScenarioMaleFeelRulesSectionName,
                        prefix + "_MinGauge",
                        0f,
                        BuildScenarioTextRuleConfigDescription("男側ゲージしきい値", order--, readOnly: false, browsable: _scenarioMaleFeelRulesExpanded))
                };
                RegisterScenarioRuleAttribute(set.LabelEntry);
                RegisterScenarioRuleAttribute(set.MinGaugeEntry);
                AttachScenarioMaleFeelRuleHandlers(set);
                _cfgScenarioMaleFeelRuleEntries.Add(set);
            }
        }

        private void EnsureScenarioSpeedRuleEntries()
        {
            List<ScenarioSpeedRule> rules = _scenarioTextRules != null ? _scenarioTextRules.SpeedLevelRules : null;
            if (rules == null)
            {
                return;
            }

            int order = 900;
            while (_cfgScenarioSpeedRuleEntries.Count < rules.Count)
            {
                int index = _cfgScenarioSpeedRuleEntries.Count;
                string prefix = "Rule" + (index + 1).ToString(CultureInfo.InvariantCulture);
                var set = new ScenarioSpeedRuleConfigEntrySet
                {
                    Index = index,
                    LabelEntry = Config.Bind(
                        ScenarioSpeedRulesSectionName,
                        prefix + "_Label",
                        string.Empty,
                        BuildScenarioTextRuleConfigDescription("Speed文言", order--, readOnly: false, browsable: _scenarioSpeedRulesExpanded)),
                    MinSpeedEntry = Config.Bind(
                        ScenarioSpeedRulesSectionName,
                        prefix + "_MinSpeed",
                        0f,
                        BuildScenarioTextRuleConfigDescription("speedしきい値", order--, readOnly: false, browsable: _scenarioSpeedRulesExpanded))
                };
                RegisterScenarioRuleAttribute(set.LabelEntry);
                RegisterScenarioRuleAttribute(set.MinSpeedEntry);
                AttachScenarioSpeedRuleHandlers(set);
                _cfgScenarioSpeedRuleEntries.Add(set);
            }
        }

        private void AttachScenarioReactionRuleHandlers(ScenarioReactionRuleConfigEntrySet set)
        {
            HookManagedConfigEntry(
                set.LabelEntry,
                "scenario-reaction-rule",
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ => ApplyScenarioReactionRuleEntry(set.Index),
                reason => SaveScenarioTextRules("config-manager:" + reason));
            HookManagedConfigEntry(
                set.MinFemaleGaugeEntry,
                "scenario-reaction-rule",
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ => ApplyScenarioReactionRuleEntry(set.Index),
                reason => SaveScenarioTextRules("config-manager:" + reason));
        }

        private void AttachScenarioMaleFeelRuleHandlers(ScenarioGaugeRuleConfigEntrySet set)
        {
            HookManagedConfigEntry(
                set.LabelEntry,
                "scenario-male-feel-rule",
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ => ApplyScenarioMaleFeelRuleEntry(set.Index),
                reason => SaveScenarioTextRules("config-manager:" + reason));
            HookManagedConfigEntry(
                set.MinGaugeEntry,
                "scenario-male-feel-rule",
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ => ApplyScenarioMaleFeelRuleEntry(set.Index),
                reason => SaveScenarioTextRules("config-manager:" + reason));
        }

        private void AttachScenarioSpeedRuleHandlers(ScenarioSpeedRuleConfigEntrySet set)
        {
            HookManagedConfigEntry(
                set.LabelEntry,
                "scenario-speed-rule",
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ => ApplyScenarioSpeedRuleEntry(set.Index),
                reason => SaveScenarioTextRules("config-manager:" + reason));
            HookManagedConfigEntry(
                set.MinSpeedEntry,
                "scenario-speed-rule",
                () => _suppressScenarioRulesConfigChangeEvent || _scenarioTextRules == null,
                _ => ApplyScenarioSpeedRuleEntry(set.Index),
                reason => SaveScenarioTextRules("config-manager:" + reason));
        }

        private bool ApplyScenarioReactionRuleEntry(int index)
        {
            if (_scenarioTextRules == null)
            {
                return false;
            }

            if (index < 0 || index >= _cfgScenarioReactionRuleEntries.Count || index >= _scenarioTextRules.ReactionLevelRules.Count)
            {
                return false;
            }

            ScenarioReactionRuleConfigEntrySet set = _cfgScenarioReactionRuleEntries[index];
            ScenarioGaugeRule rule = _scenarioTextRules.ReactionLevelRules[index];
            rule.Label = set.LabelEntry != null ? set.LabelEntry.Value : rule.Label;
            rule.MinGauge = set.MinFemaleGaugeEntry != null ? Mathf.Clamp(set.MinFemaleGaugeEntry.Value, 0f, 100f) : rule.MinGauge;
            rule.Normalize();
            return true;
        }

        private bool ApplyScenarioMaleFeelRuleEntry(int index)
        {
            if (_scenarioTextRules == null)
            {
                return false;
            }

            if (index < 0 || index >= _cfgScenarioMaleFeelRuleEntries.Count || index >= _scenarioTextRules.MaleFeelLevelRules.Count)
            {
                return false;
            }

            ScenarioGaugeRuleConfigEntrySet set = _cfgScenarioMaleFeelRuleEntries[index];
            ScenarioGaugeRule rule = _scenarioTextRules.MaleFeelLevelRules[index];
            rule.Label = set.LabelEntry != null ? set.LabelEntry.Value : rule.Label;
            rule.MinGauge = set.MinGaugeEntry != null ? Mathf.Clamp(set.MinGaugeEntry.Value, 0f, 100f) : rule.MinGauge;
            rule.Normalize();
            return true;
        }

        private bool ApplyScenarioSpeedRuleEntry(int index)
        {
            if (_scenarioTextRules == null)
            {
                return false;
            }

            if (index < 0 || index >= _cfgScenarioSpeedRuleEntries.Count || index >= _scenarioTextRules.SpeedLevelRules.Count)
            {
                return false;
            }

            ScenarioSpeedRuleConfigEntrySet set = _cfgScenarioSpeedRuleEntries[index];
            ScenarioSpeedRule rule = _scenarioTextRules.SpeedLevelRules[index];
            rule.Label = set.LabelEntry != null ? set.LabelEntry.Value : rule.Label;
            rule.MinSpeed = set.MinSpeedEntry != null ? Mathf.Clamp01(set.MinSpeedEntry.Value) : rule.MinSpeed;
            rule.Normalize();
            return true;
        }

        private void SyncScenarioTextRuleConfigEntriesFromRuntime()
        {
            if (_scenarioTextRules == null)
            {
                return;
            }

            _suppressScenarioRulesConfigChangeEvent = true;
            try
            {
                if (_cfgScenarioReactionRulesExpanded != null)
                {
                    _cfgScenarioReactionRulesExpanded.Value = _scenarioReactionRulesExpanded;
                }
                if (_cfgScenarioMaleFeelRulesExpanded != null)
                {
                    _cfgScenarioMaleFeelRulesExpanded.Value = _scenarioMaleFeelRulesExpanded;
                }
                if (_cfgScenarioSpeedRulesExpanded != null)
                {
                    _cfgScenarioSpeedRulesExpanded.Value = _scenarioSpeedRulesExpanded;
                }

                for (int i = 0; i < _cfgScenarioReactionRuleEntries.Count && i < _scenarioTextRules.ReactionLevelRules.Count; i++)
                {
                    ScenarioReactionRuleConfigEntrySet set = _cfgScenarioReactionRuleEntries[i];
                    ScenarioGaugeRule rule = _scenarioTextRules.ReactionLevelRules[i];
                    if (set.LabelEntry != null) set.LabelEntry.Value = rule.Label;
                    if (set.MinFemaleGaugeEntry != null) set.MinFemaleGaugeEntry.Value = rule.MinGauge;
                }

                for (int i = 0; i < _cfgScenarioMaleFeelRuleEntries.Count && i < _scenarioTextRules.MaleFeelLevelRules.Count; i++)
                {
                    ScenarioGaugeRuleConfigEntrySet set = _cfgScenarioMaleFeelRuleEntries[i];
                    ScenarioGaugeRule rule = _scenarioTextRules.MaleFeelLevelRules[i];
                    if (set.LabelEntry != null) set.LabelEntry.Value = rule.Label;
                    if (set.MinGaugeEntry != null) set.MinGaugeEntry.Value = rule.MinGauge;
                }

                for (int i = 0; i < _cfgScenarioSpeedRuleEntries.Count && i < _scenarioTextRules.SpeedLevelRules.Count; i++)
                {
                    ScenarioSpeedRuleConfigEntrySet set = _cfgScenarioSpeedRuleEntries[i];
                    ScenarioSpeedRule rule = _scenarioTextRules.SpeedLevelRules[i];
                    if (set.LabelEntry != null) set.LabelEntry.Value = rule.Label;
                    if (set.MinSpeedEntry != null) set.MinSpeedEntry.Value = rule.MinSpeed;
                }
            }
            finally
            {
                _suppressScenarioRulesConfigChangeEvent = false;
            }
        }

        private void UpdateScenarioTextRuleConfigEntryState()
        {
            foreach (var pair in _cfgScenarioRulesAttributes)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                if (_cfgScenarioReactionRulesExpanded != null && ReferenceEquals(pair.Key, _cfgScenarioReactionRulesExpanded))
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = true;
                    continue;
                }

                if (_cfgScenarioMaleFeelRulesExpanded != null && ReferenceEquals(pair.Key, _cfgScenarioMaleFeelRulesExpanded))
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = true;
                    continue;
                }
                if (_cfgScenarioSpeedRulesExpanded != null && ReferenceEquals(pair.Key, _cfgScenarioSpeedRulesExpanded))
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = true;
                    continue;
                }

                bool isReactionEntry = false;
                for (int i = 0; i < _cfgScenarioReactionRuleEntries.Count; i++)
                {
                    ScenarioReactionRuleConfigEntrySet set = _cfgScenarioReactionRuleEntries[i];
                    if (ReferenceEquals(set.LabelEntry, pair.Key)
                        || ReferenceEquals(set.MinFemaleGaugeEntry, pair.Key))
                    {
                        isReactionEntry = true;
                        break;
                    }
                }
                if (isReactionEntry)
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = _scenarioReactionRulesExpanded;
                    continue;
                }

                bool isMaleFeelEntry = false;
                for (int i = 0; i < _cfgScenarioMaleFeelRuleEntries.Count; i++)
                {
                    ScenarioGaugeRuleConfigEntrySet set = _cfgScenarioMaleFeelRuleEntries[i];
                    if (ReferenceEquals(set.LabelEntry, pair.Key)
                        || ReferenceEquals(set.MinGaugeEntry, pair.Key))
                    {
                        isMaleFeelEntry = true;
                        break;
                    }
                }
                if (isMaleFeelEntry)
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = _scenarioMaleFeelRulesExpanded;
                    continue;
                }

                bool isSpeedEntry = false;
                for (int i = 0; i < _cfgScenarioSpeedRuleEntries.Count; i++)
                {
                    ScenarioSpeedRuleConfigEntrySet set = _cfgScenarioSpeedRuleEntries[i];
                    if (ReferenceEquals(set.LabelEntry, pair.Key)
                        || ReferenceEquals(set.MinSpeedEntry, pair.Key))
                    {
                        isSpeedEntry = true;
                        break;
                    }
                }
                if (isSpeedEntry)
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = _scenarioSpeedRulesExpanded;
                }
            }
        }

        private ConfigDescription BuildScenarioTextToggleButtonDescription(string openLabel, string closeLabel, int order, bool readOnly)
        {
            var attrs = new ConfigurationManagerAttributes
            {
                Order = order,
                ReadOnly = readOnly,
                HideSettingName = true,
                HideDefaultButton = true
            };
            attrs.CustomDrawer = entryBase =>
            {
                var boolEntry = entryBase as ConfigEntry<bool>;
                if (boolEntry == null)
                {
                    return;
                }

                bool prevEnabled = GUI.enabled;
                if (attrs.ReadOnly == true)
                {
                    GUI.enabled = false;
                }

                string buttonText = boolEntry.Value ? closeLabel : openLabel;
                if (GUILayout.Button(buttonText, GUILayout.ExpandWidth(true)))
                {
                    boolEntry.Value = !boolEntry.Value;
                }

                GUI.enabled = prevEnabled;
            };
            return new ConfigDescription(string.Empty, null, attrs);
        }

        private ConfigDescription BuildScenarioTextRuleConfigDescription(string description, int order, bool readOnly, bool browsable)
        {
            var attrs = new ConfigurationManagerAttributes
            {
                Order = order,
                ReadOnly = readOnly,
                Browsable = browsable
            };
            return new ConfigDescription(description, null, attrs);
        }

        private void RegisterScenarioRuleAttribute(ConfigEntryBase entryBase)
        {
            if (entryBase == null || entryBase.Description == null || entryBase.Description.Tags == null)
            {
                return;
            }

            foreach (object tag in entryBase.Description.Tags)
            {
                var attr = tag as ConfigurationManagerAttributes;
                if (attr != null)
                {
                    _cfgScenarioRulesAttributes[entryBase] = attr;
                    break;
                }
            }
        }

        private void SaveScenarioTextRules(string reason)
        {
            if (_scenarioTextRules == null || string.IsNullOrWhiteSpace(PluginDir))
            {
                return;
            }

            try
            {
                ScenarioTextRulesStore.Save(ScenarioTextRulesStore.GetPath(PluginDir), _scenarioTextRules);
                SyncScenarioTemplateConfigEntriesFromRuntime();
                Log("[scenario-rules] saved reason=" + reason);
            }
            catch (Exception ex)
            {
                LogWarn("[scenario-rules] save failed reason=" + reason + " message=" + ex.Message);
            }
        }

        private void ApplyScenarioTextSettingsToConfigEntries(PluginSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (_cfgScenarioTextEnabled != null) _cfgScenarioTextEnabled.Value = settings.ScenarioTextEnabled;
            if (_cfgScenarioManualSendRequested != null) _cfgScenarioManualSendRequested.Value = settings.ScenarioManualSendRequested;
            if (_cfgScenarioAutoSendEnabled != null) _cfgScenarioAutoSendEnabled.Value = settings.ScenarioAutoSendEnabled;
            if (_cfgScenarioAutoSendIntervalSeconds != null) _cfgScenarioAutoSendIntervalSeconds.Value = Mathf.Clamp(settings.ScenarioAutoSendIntervalSeconds, 2f, 300f);
            SyncScenarioTemplateConfigEntriesFromRuntime();
        }

        private void ApplyScenarioTextConfigEntriesToSettings()
        {
            if (Settings == null)
            {
                return;
            }

            if (_cfgScenarioTextEnabled != null) Settings.ScenarioTextEnabled = _cfgScenarioTextEnabled.Value;
            if (_cfgScenarioManualSendRequested != null) Settings.ScenarioManualSendRequested = _cfgScenarioManualSendRequested.Value;
            if (_cfgScenarioAutoSendEnabled != null) Settings.ScenarioAutoSendEnabled = _cfgScenarioAutoSendEnabled.Value;
            if (_cfgScenarioAutoSendIntervalSeconds != null) Settings.ScenarioAutoSendIntervalSeconds = Mathf.Clamp(_cfgScenarioAutoSendIntervalSeconds.Value, 2f, 300f);
        }

        private void SyncScenarioTemplateConfigEntriesFromRuntime()
        {
            if (_scenarioTextRules == null)
            {
                return;
            }

            _suppressScenarioRulesConfigChangeEvent = true;
            try
            {
                if (_cfgScenarioManualTemplate != null) _cfgScenarioManualTemplate.Value = _scenarioTextRules.ManualTemplate;
                if (_cfgScenarioAutoTemplate != null) _cfgScenarioAutoTemplate.Value = _scenarioTextRules.AutoTemplate;
                if (_cfgScenarioCurrentTemplate != null) _cfgScenarioCurrentTemplate.Value = _scenarioTextRules.CurrentTemplate;
                if (_cfgScenarioRecentTemplate != null) _cfgScenarioRecentTemplate.Value = _scenarioTextRules.RecentTemplate;
                if (_cfgScenarioMaleFeelTemplate != null) _cfgScenarioMaleFeelTemplate.Value = _scenarioTextRules.MaleFeelTemplate;
            }
            finally
            {
                _suppressScenarioRulesConfigChangeEvent = false;
            }
        }

        private void TickScenarioTextSend(float now)
        {
            PluginSettings settings = Settings;
            if (settings == null)
            {
                return;
            }

            if (settings.ScenarioManualSendRequested)
            {
                ClearScenarioManualSendRequest();
                string reason;
                SendScenarioTextNow(settings, "manual", forceOutsideScene: true, requireEnabled: false, out reason);
            }

            if (!settings.ScenarioTextEnabled || !settings.ScenarioAutoSendEnabled)
            {
                return;
            }

            if (now < _nextScenarioAutoSendTime)
            {
                return;
            }

            float interval = Mathf.Clamp(settings.ScenarioAutoSendIntervalSeconds, 2f, 300f);
            _nextScenarioAutoSendTime = now + interval;
            string autoReason;
            SendScenarioTextNow(settings, "auto", forceOutsideScene: false, requireEnabled: true, out autoReason);
        }

        public static bool TrySendScenarioTextManualNow(string source, out string reason)
        {
            reason = null;
            Plugin instance = Instance;
            if (instance == null)
            {
                reason = "VoiceFaceEventBridge instance is not available";
                return false;
            }

            PluginSettings settings = Settings;
            if (settings == null)
            {
                reason = "VoiceFaceEventBridge settings are not available";
                return false;
            }

            return instance.SendScenarioTextNow(settings, "manual", forceOutsideScene: true, requireEnabled: false, out reason);
        }

        public static bool TryGetScenarioTextAutoSendEnabled(out bool enabled)
        {
            enabled = false;
            PluginSettings settings = Settings;
            if (settings == null)
            {
                return false;
            }

            enabled = settings.ScenarioTextEnabled && settings.ScenarioAutoSendEnabled;
            return true;
        }

        public static bool TrySetScenarioTextAutoSendEnabled(bool enabled, string source, out string reason)
        {
            reason = null;
            Plugin instance = Instance;
            if (instance == null)
            {
                reason = "VoiceFaceEventBridge instance is not available";
                return false;
            }

            return instance.SetScenarioTextAutoSendEnabled(enabled, source, out reason);
        }

        private bool SetScenarioTextAutoSendEnabled(bool enabled, string source, out string reason)
        {
            reason = null;
            if (Settings == null)
            {
                reason = "settings are not available";
                return false;
            }

            if (enabled)
            {
                Settings.ScenarioTextEnabled = true;
            }
            Settings.ScenarioAutoSendEnabled = enabled;

            _suppressConfigChangeEvent = true;
            try
            {
                if (_cfgScenarioTextEnabled != null)
                {
                    _cfgScenarioTextEnabled.Value = Settings.ScenarioTextEnabled;
                }
                if (_cfgScenarioAutoSendEnabled != null)
                {
                    _cfgScenarioAutoSendEnabled.Value = enabled;
                }
            }
            finally
            {
                _suppressConfigChangeEvent = false;
            }

            SaveSettingsToConfigJson("scenario-auto-toggle:" + (source ?? string.Empty));
            SaveConfigFile("scenario-auto-toggle");
            Log("[scenario] auto send toggled source=" + (source ?? string.Empty) + " enabled=" + enabled);
            return true;
        }

        private void ClearScenarioManualSendRequest()
        {
            if (Settings != null)
            {
                Settings.ScenarioManualSendRequested = false;
            }

            if (_cfgScenarioManualSendRequested != null && _cfgScenarioManualSendRequested.Value)
            {
                _suppressConfigChangeEvent = true;
                try
                {
                    _cfgScenarioManualSendRequested.Value = false;
                }
                finally
                {
                    _suppressConfigChangeEvent = false;
                }
            }

            SaveSettingsToConfigJson("scenario-manual-reset");
            SaveConfigFile("scenario-manual-reset");
        }

        private bool SendScenarioTextNow(
            PluginSettings settings,
            string sendMode,
            bool forceOutsideScene,
            bool requireEnabled,
            out string failureReason)
        {
            failureReason = null;
            if (settings == null)
            {
                failureReason = "settings are not available";
                return false;
            }

            if (requireEnabled && !settings.ScenarioTextEnabled)
            {
                LogWarn("[scenario] send skipped: ScenarioText.Enabled is false mode=" + sendMode);
                failureReason = "ScenarioText.Enabled is false";
                return false;
            }

            ScenarioStateSnapshot snapshot = CaptureScenarioStateSnapshot(_lastScenarioSnapshot);
            if (!snapshot.HasHScene && !forceOutsideScene)
            {
                LogScenarioSkip("[scenario] auto send skipped: HSceneProc not available");
                _lastScenarioSnapshot = snapshot;
                failureReason = "HSceneProc not available";
                return false;
            }

            string text = ScenarioTextComposer.Compose(snapshot, settings, _scenarioTextRules, sendMode);
            if (string.IsNullOrWhiteSpace(text))
            {
                LogWarn("[scenario] send skipped: composed text is empty mode=" + sendMode);
                _lastScenarioSnapshot = snapshot;
                failureReason = "composed text is empty";
                return false;
            }

            string traceId = "scenario_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            string reason;
            if (!TryForwardScenarioTextViaSubtitleCore(text, sendMode, traceId, out reason))
            {
                LogScenarioSkip("[scenario] send skipped: SubtitleCore forward failed mode=" + sendMode
                    + " trace=" + traceId
                    + " reason=" + reason
                    + " text=" + TrimPreview(text, 100));
                _lastScenarioSnapshot = snapshot;
                failureReason = reason;
                return false;
            }

            Log("[scenario] forwarded via SubtitleCore mode=" + sendMode + " trace=" + traceId + " text=" + TrimPreview(text, 120));
            _lastScenarioSnapshot = snapshot;
            return true;
        }

        private bool TryForwardScenarioTextViaSubtitleCore(string text, string sendMode, string traceId, out string reason)
        {
            reason = null;
            try
            {
                Type apiType = FindSubtitleApiType();
                if (apiType == null)
                {
                    reason = "MainGameSubtitleCore.SubtitleApi not found";
                    return false;
                }

                MethodInfo method = apiType.GetMethod(
                    "TryForwardText",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string), typeof(string).MakeByRefType() },
                    null);
                if (method == null)
                {
                    reason = "SubtitleApi.TryForwardText(text, source, out reason) not found";
                    return false;
                }

                object[] args = { text, "voiceface_scenario", null };
                bool ok = method.Invoke(null, args) is bool result && result;
                reason = args[2] as string;
                if (!ok && string.IsNullOrWhiteSpace(reason))
                {
                    reason = "SubtitleCore rejected forward request";
                }

                return ok;
            }
            catch (TargetInvocationException ex)
            {
                reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                LogWarn("[scenario] SubtitleCore forward invocation failed mode=" + sendMode + " trace=" + traceId + " reason=" + reason);
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                LogWarn("[scenario] SubtitleCore forward failed mode=" + sendMode + " trace=" + traceId + " reason=" + reason);
                return false;
            }
        }

        private static Type FindSubtitleApiType()
        {
            Type apiType = Type.GetType("MainGameSubtitleCore.SubtitleApi, MainGameSubtitleCore", false);
            if (apiType != null)
            {
                return apiType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                AssemblyName name = assembly.GetName();
                if (name == null || !string.Equals(name.Name, "MainGameSubtitleCore", StringComparison.Ordinal))
                {
                    continue;
                }

                apiType = assembly.GetType("MainGameSubtitleCore.SubtitleApi", false);
                if (apiType != null)
                {
                    return apiType;
                }
            }

            return null;
        }

        private ScenarioStateSnapshot CaptureScenarioStateSnapshot(ScenarioStateSnapshot previous)
        {
            var snapshot = new ScenarioStateSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                HasHScene = false,
                ModeName = "none",
                ModeValue = -1,
                PostureName = "Hシーン外",
                ActionLabel = "待機",
                ReactionLabel = "未取得",
                MaleFeelLabel = "未取得",
                SpeedFeelLabel = "未取得",
                CurrentSongLabel = ResolveScenarioCurrentSongLabel(),
                RecentChangeText = "Hシーン外のため変化は取得していない。",
                KissLabel = "キス状態は未取得",
                ArousalLabel = "ゲージ未取得",
                SensitivityLabel = "感度は未取得",
                SpeedCalc = 0f,
                FemaleGauge = 0f,
                MaleGauge = 0f
            };

            HSceneProc proc = CurrentProc ?? FindCurrentProc();
            if (proc == null || proc.flags == null)
            {
                return snapshot;
            }

            HFlag flags = proc.flags;
            HFlag.EMode mode = flags.mode;
            snapshot.HasHScene = true;
            snapshot.ModeName = mode.ToString();
            snapshot.ModeValue = (int)mode;
            snapshot.PostureName = ResolveScenarioPostureName(proc);
            snapshot.ActionLabel = ResolveScenarioActionLabel(mode);
            snapshot.SpeedCalc = Mathf.Clamp01(flags.speedCalc);
            snapshot.FemaleGauge = Mathf.Clamp(flags.gaugeFemale, 0f, 100f);
            snapshot.MaleGauge = Mathf.Clamp(flags.gaugeMale, 0f, 100f);
            snapshot.ReactionLabel = ResolveScenarioReactionLabel(snapshot.FemaleGauge);
            snapshot.MaleFeelLabel = ResolveScenarioMaleFeelLabel(snapshot.MaleGauge);
            snapshot.SpeedFeelLabel = ResolveScenarioSpeedFeelLabel(snapshot.SpeedCalc);
            snapshot.ArousalLabel = "女の子ゲージ" + snapshot.FemaleGauge.ToString("0", CultureInfo.InvariantCulture)
                + "%、男ゲージ" + snapshot.MaleGauge.ToString("0", CultureInfo.InvariantCulture) + "%";
            snapshot.SensitivityLabel = ResolveScenarioSensitivityLabel(snapshot.FemaleGauge);
            snapshot.KissLabel = ShouldBlockKissActions() ? "キス干渉は抑制中" : "キス干渉は通常";
            snapshot.RecentChangeText = ResolveScenarioRecentChangeText(previous, snapshot);
            return snapshot;
        }

        private static string ResolveScenarioCurrentSongLabel()
        {
            string path = TryGetBlankMapAddCurrentVideoPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return "曲は未取得";
            }

            string normalized = path.Trim();
            try
            {
                if (normalized.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = Uri.UnescapeDataString(normalized.Substring(8));
                }

                string fileName = Path.GetFileNameWithoutExtension(normalized);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName.Trim();
                }
            }
            catch
            {
            }

            return normalized;
        }

        private static string TryGetBlankMapAddCurrentVideoPath()
        {
            try
            {
                Type pluginType = Type.GetType("MainGameBlankMapAdd.Plugin, MainGameBlankMapAdd", false);
                if (pluginType == null)
                {
                    return null;
                }

                MethodInfo method = pluginType.GetMethod(
                    "GetCurrentVideoPath",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null || method.ReturnType != typeof(string))
                {
                    return null;
                }

                return method.Invoke(null, null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveScenarioPostureName(HSceneProc proc)
        {
            try
            {
                if (proc != null && proc.flags != null && proc.flags.nowAnimationInfo != null)
                {
                    string name = proc.flags.nowAnimationInfo.nameAnimation;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name.Trim();
                    }
                }
            }
            catch
            {
            }

            try
            {
                string stateName = proc != null && proc.flags != null ? proc.flags.nowAnimStateName : string.Empty;
                if (!string.IsNullOrWhiteSpace(stateName))
                {
                    return stateName.Trim();
                }
            }
            catch
            {
            }

            return "不明な体位";
        }

        private static string ResolveScenarioActionLabel(HFlag.EMode mode)
        {
            if (IsSonyuPoseMode(mode))
            {
                return "挿入";
            }

            if (IsHoushiPoseMode(mode))
            {
                return "奉仕";
            }

            if (mode == HFlag.EMode.aibu)
            {
                return "愛撫";
            }

            if (mode == HFlag.EMode.masturbation)
            {
                return "オナニー";
            }

            if (mode == HFlag.EMode.lesbian)
            {
                return "レズ";
            }

            if (mode == HFlag.EMode.peeping)
            {
                return "覗き";
            }

            return "Hシーン";
        }

        private string ResolveScenarioMaleFeelLabel(float maleGauge)
        {
            if (_scenarioTextRules != null && _scenarioTextRules.MaleFeelLevelRules != null)
            {
                for (int i = 0; i < _scenarioTextRules.MaleFeelLevelRules.Count; i++)
                {
                    ScenarioGaugeRule rule = _scenarioTextRules.MaleFeelLevelRules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    if (maleGauge >= rule.MinGauge)
                    {
                        return rule.Label;
                    }
                }
            }
            return "まだ余裕がある";
        }

        private string ResolveScenarioReactionLabel(float femaleGauge)
        {
            if (_scenarioTextRules != null && _scenarioTextRules.ReactionLevelRules != null)
            {
                for (int i = 0; i < _scenarioTextRules.ReactionLevelRules.Count; i++)
                {
                    ScenarioGaugeRule rule = _scenarioTextRules.ReactionLevelRules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    if (femaleGauge >= rule.MinGauge)
                    {
                        return rule.Label;
                    }
                }
            }

            return "落ち着いている";
        }

        private string ResolveScenarioSpeedFeelLabel(float speed)
        {
            if (_scenarioTextRules != null && _scenarioTextRules.SpeedLevelRules != null)
            {
                for (int i = 0; i < _scenarioTextRules.SpeedLevelRules.Count; i++)
                {
                    ScenarioSpeedRule rule = _scenarioTextRules.SpeedLevelRules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    if (speed >= rule.MinSpeed)
                    {
                        return rule.Label;
                    }
                }
            }

            return "かなりゆっくり";
        }

        private static string ResolveScenarioSensitivityLabel(float femaleGauge)
        {
            if (femaleGauge >= 90f)
            {
                return "かなり敏感";
            }

            if (femaleGauge >= 70f)
            {
                return "敏感";
            }

            if (femaleGauge >= 35f)
            {
                return "反応が高まり中";
            }

            return "落ち着いている";
        }

        private static string ResolveScenarioRecentChangeText(ScenarioStateSnapshot previous, ScenarioStateSnapshot current)
        {
            if (current == null || !current.HasHScene)
            {
                return "Hシーン外のため変化は取得していない。";
            }

            if (previous == null || !previous.HasHScene)
            {
                return "現在の場面を取得したところ。";
            }

            if (!string.Equals(previous.PostureName, current.PostureName, StringComparison.Ordinal))
            {
                return "体位は" + previous.PostureName + "から" + current.PostureName + "へ変わった。";
            }

            if (!string.Equals(previous.ActionLabel, current.ActionLabel, StringComparison.Ordinal))
            {
                return "流れは" + previous.ActionLabel + "から" + current.ActionLabel + "へ変わった。";
            }

            return "大きな変化はなく、同じ流れが続いている。";
        }

        private void LogScenarioSkip(string message)
        {
            float now = Time.unscaledTime;
            if (now < _nextScenarioSkipLogTime)
            {
                return;
            }

            _nextScenarioSkipLogTime = now + 30f;
            LogWarn(message);
        }
    }
}
