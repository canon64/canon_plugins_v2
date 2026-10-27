using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.Setup.cs
    //
    // 責務: BepInEx ConfigEntry 群の定義/同期、設定UI (Configuration Manager) 連携、
    //       設定 JSON / pose 分類 JSON の読み書き、キーバインド解決、設定永続化。
    //
    // ここには「設定そのもの」だけを置き、コマンド処理本体は Plugin.Handlers.cs、
    // 配線 (Awake/Update/dispatch) は Plugin.cs (本体) に残す。
    internal sealed partial class Plugin
    {
        private void EnsurePoseControlConfigEntries()
        {
            EnsurePoseGlobalConfigEntry();
            EnsurePoseCategoryConfigEntries();
            EnsurePoseRuleConfigEntries();
            EnsurePoseInferRuleConfigEntries();
            UpdatePoseControlReadOnlyState();
        }

        private void EnsurePoseGlobalConfigEntry()
        {
            if (_cfgPoseChangeEnabled != null)
            {
                EnsurePoseSimpleModeConfigEntries();
                return;
            }

            _cfgPoseChangeEnabled = Config.Bind(
                PoseControlSectionName,
                "【最上位】体位変更を有効化",
                _poseChangeEnabled,
                BuildPoseControlConfigDescription("このチェックがOFFの間は、体位変更の全項目が無効（グレーアウト）になります。", order: 1100, readOnly: false, isAdvanced: false));
            RegisterPoseReadonlyAttribute(_cfgPoseChangeEnabled);
            HookManagedConfigEntry(
                _cfgPoseChangeEnabled,
                "pose-global-toggle",
                () => _suppressPoseConfigChangeEvent,
                value =>
                {
                    _poseChangeEnabled = value;
                    if (_poseChangeEnabled)
                    {
                        ExpandPoseSectionsOnEnable();
                    }
                    UpdatePoseControlReadOnlyState();
                    return true;
                },
                reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                refreshConfigManager: true);

            EnsurePoseSimpleModeConfigEntries();
        }

        private void EnsurePoseSimpleModeConfigEntries()
        {
            _poseSimpleModeTriggerKeywords = NormalizePoseSimpleModeTriggerKeywords(_poseSimpleModeTriggerKeywords);

            if (_cfgPoseSimpleModeEnabled == null)
            {
                _cfgPoseSimpleModeEnabled = Config.Bind(
                    PoseControlSectionName,
                    "【簡易】シンプル体位モード",
                    _poseSimpleModeEnabled,
                    BuildPoseControlConfigDescription("ONで「体位名 + になるね」の同一行判定のみ使用（複雑ルール/推定は完全バイパス）。", order: 1090, readOnly: !_poseChangeEnabled, isAdvanced: false));
                RegisterPoseReadonlyAttribute(_cfgPoseSimpleModeEnabled);
                HookManagedConfigEntry(
                    _cfgPoseSimpleModeEnabled,
                    "pose-simple-mode-toggle",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseSimpleModeEnabled = value;
                        UpdatePoseControlReadOnlyState();
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                    refreshConfigManager: true);
            }

            if (_cfgPoseSimpleModeTriggerKeywords == null)
            {
                _cfgPoseSimpleModeTriggerKeywords = Config.Bind(
                    PoseControlSectionName,
                    "【簡易】体位変更キーワード",
                    _poseSimpleModeTriggerKeywords,
                    BuildPoseControlConfigDescription("シンプル体位モードの発火語（カンマ区切り）。同じ行に体位名とこの語があると変更。", order: 1085, readOnly: !_poseChangeEnabled, isAdvanced: false));
                RegisterPoseReadonlyAttribute(_cfgPoseSimpleModeTriggerKeywords);
                HookManagedConfigEntry(
                    _cfgPoseSimpleModeTriggerKeywords,
                    "pose-simple-trigger",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseSimpleModeTriggerKeywords = NormalizePoseSimpleModeTriggerKeywords(value);
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason));
            }
        }

        private void EnsurePoseCategoryConfigEntries()
        {
            EnsurePoseCategorySectionControlEntries();

            var categories = _poseCategoryEnabled.Keys
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            int order = 900;
            foreach (string category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                if (_cfgPoseCategoryEnabledEntries.ContainsKey(category))
                {
                    order--;
                    continue;
                }

                bool enabled = IsPoseCategoryEnabled(category);
                var entry = Config.Bind(
                    PoseControlCategoriesSectionName,
                    category,
                    enabled,
                    BuildPoseControlConfigDescription($"カテゴリ '{category}' の有効/無効。", order, readOnly: !_poseChangeEnabled));
                _cfgPoseCategoryEnabledEntries[category] = entry;
                RegisterPoseReadonlyAttribute(entry);
                HookManagedConfigEntry(
                    entry,
                    "pose-category",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseCategoryEnabled[category] = value;
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                    refreshConfigManager: true);
                order--;
            }
        }

        private void EnsurePoseCategorySectionControlEntries()
        {
            if (_cfgPoseCategoriesExpanded == null)
            {
                _cfgPoseCategoriesExpanded = Config.Bind(
                    PoseControlCategoriesSectionName,
                    "【表示】カテゴリ一覧",
                    _poseCategoriesExpanded,
                    BuildPoseControlToggleButtonDescription(
                        openLabel: "カテゴリ一覧を開く",
                        closeLabel: "カテゴリ一覧を閉じる",
                        order: 990,
                        readOnly: !_poseChangeEnabled));
                RegisterPoseReadonlyAttribute(_cfgPoseCategoriesExpanded);
                HookManagedConfigEntry(
                    _cfgPoseCategoriesExpanded,
                    "pose-category-expand-toggle",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseCategoriesExpanded = value;
                        UpdatePoseControlReadOnlyState();
                        return true;
                    },
                    saveValue: null,
                    refreshConfigManager: true);
            }
            _poseCategoriesExpanded = _cfgPoseCategoriesExpanded != null && _cfgPoseCategoriesExpanded.Value;
        }

        private void EnsurePoseRuleConfigEntries()
        {
            EnsurePoseRuleSectionControlEntries();

            var rules = (_poseKeywordScoreRules ?? new List<PoseKeywordScoreRule>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RuleId))
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.RuleId, StringComparer.Ordinal)
                .ToArray();

            int order = 800;
            int index = 1;
            foreach (PoseKeywordScoreRule rule in rules)
            {
                if (_cfgPoseRuleEnabledEntries.ContainsKey(rule.RuleId))
                {
                    order--;
                    index++;
                    continue;
                }

                bool enabled = rule.Enabled != false;
                string key = BuildPoseRuleEntryLabel(rule, index);
                string ruleId = rule.RuleId;
                var entry = Config.Bind(
                    PoseControlRulesSectionName,
                    key,
                    enabled,
                    BuildPoseControlConfigDescription($"[{rule.Category}] ID={ruleId}", order, readOnly: !_poseChangeEnabled || !_poseRulesEnabled));
                _cfgPoseRuleEnabledEntries[ruleId] = entry;
                RegisterPoseReadonlyAttribute(entry);
                HookManagedConfigEntry(
                    entry,
                    "pose-rule",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        PoseKeywordScoreRule target = _poseKeywordScoreRules.FirstOrDefault(x => x != null && string.Equals(x.RuleId, ruleId, StringComparison.Ordinal));
                        if (target != null)
                        {
                            target.Enabled = value;
                        }
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                    refreshConfigManager: true);
                order--;
                index++;
            }
        }

        private void EnsurePoseRuleSectionControlEntries()
        {
            if (_cfgPoseRulesEnabled == null)
            {
                _cfgPoseRulesEnabled = Config.Bind(
                    PoseControlRulesSectionName,
                    "【全体】ルールを有効化",
                    _poseRulesEnabled,
                    BuildPoseControlConfigDescription("体位ルール一覧をまとめてON/OFFします。", order: 1000, readOnly: false, isAdvanced: false));
                RegisterPoseReadonlyAttribute(_cfgPoseRulesEnabled);
                HookManagedConfigEntry(
                    _cfgPoseRulesEnabled,
                    "pose-rules-global",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseRulesEnabled = value;
                        if (_poseRulesEnabled && !_poseRulesExpanded)
                        {
                            SetPoseSectionExpanded(ref _poseRulesExpanded, _cfgPoseRulesExpanded, true);
                        }
                        UpdatePoseControlReadOnlyState();
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                    refreshConfigManager: true);
            }

            if (_cfgPoseRulesExpanded == null)
            {
                _cfgPoseRulesExpanded = Config.Bind(
                    PoseControlRulesSectionName,
                    "【表示】ルール一覧",
                    _poseRulesExpanded,
                    BuildPoseControlToggleButtonDescription(
                        openLabel: "ルール一覧を開く",
                        closeLabel: "ルール一覧を閉じる",
                        order: 990,
                        readOnly: !_poseChangeEnabled || !_poseRulesEnabled));
                RegisterPoseReadonlyAttribute(_cfgPoseRulesExpanded);
                HookManagedConfigEntry(
                    _cfgPoseRulesExpanded,
                    "pose-rules-expand-toggle",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseRulesExpanded = value;
                        UpdatePoseControlReadOnlyState();
                        return true;
                    },
                    saveValue: null,
                    refreshConfigManager: true);
            }
            _poseRulesExpanded = _cfgPoseRulesExpanded != null && _cfgPoseRulesExpanded.Value;
        }

        private void EnsurePoseInferRuleConfigEntries()
        {
            EnsurePoseInferSectionControlEntries();

            var rules = (_poseCategoryInferRules ?? new List<PoseCategoryInferRule>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RuleId))
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.RuleId, StringComparer.Ordinal)
                .ToArray();

            int order = 700;
            int index = 1;
            foreach (PoseCategoryInferRule rule in rules)
            {
                if (_cfgPoseInferRuleEnabledEntries.ContainsKey(rule.RuleId))
                {
                    order--;
                    index++;
                    continue;
                }

                bool enabled = rule.Enabled != false;
                string key = BuildPoseInferRuleEntryLabel(rule, index);
                string ruleId = rule.RuleId;
                var entry = Config.Bind(
                    PoseControlInferRulesSectionName,
                    key,
                    enabled,
                    BuildPoseControlConfigDescription($"[{rule.TargetCategory}] ID={ruleId}", order, readOnly: !_poseChangeEnabled || !_poseInferRulesEnabled));
                _cfgPoseInferRuleEnabledEntries[ruleId] = entry;
                RegisterPoseReadonlyAttribute(entry);
                HookManagedConfigEntry(
                    entry,
                    "pose-infer-rule",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        PoseCategoryInferRule target = _poseCategoryInferRules.FirstOrDefault(x => x != null && string.Equals(x.RuleId, ruleId, StringComparison.Ordinal));
                        if (target != null)
                        {
                            target.Enabled = value;
                        }
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                    refreshConfigManager: true);
                order--;
                index++;
            }
        }

        private void EnsurePoseInferSectionControlEntries()
        {
            if (_cfgPoseInferRulesEnabled == null)
            {
                _cfgPoseInferRulesEnabled = Config.Bind(
                    PoseControlInferRulesSectionName,
                    "【全体】推定ルールを有効化",
                    _poseInferRulesEnabled,
                    BuildPoseControlConfigDescription("体位推定ルール一覧をまとめてON/OFFします。", order: 1000, readOnly: false, isAdvanced: false));
                RegisterPoseReadonlyAttribute(_cfgPoseInferRulesEnabled);
                HookManagedConfigEntry(
                    _cfgPoseInferRulesEnabled,
                    "pose-infer-global",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseInferRulesEnabled = value;
                        if (_poseInferRulesEnabled && !_poseInferRulesExpanded)
                        {
                            SetPoseSectionExpanded(ref _poseInferRulesExpanded, _cfgPoseInferRulesExpanded, true);
                        }
                        UpdatePoseControlReadOnlyState();
                        return true;
                    },
                    reason => SaveCurrentPoseScoreRulesToFile("config-manager:" + reason),
                    refreshConfigManager: true);
            }

            if (_cfgPoseInferRulesExpanded == null)
            {
                _cfgPoseInferRulesExpanded = Config.Bind(
                    PoseControlInferRulesSectionName,
                    "【表示】推定ルール一覧",
                    _poseInferRulesExpanded,
                    BuildPoseControlToggleButtonDescription(
                        openLabel: "推定ルール一覧を開く",
                        closeLabel: "推定ルール一覧を閉じる",
                        order: 990,
                        readOnly: !_poseChangeEnabled || !_poseInferRulesEnabled));
                RegisterPoseReadonlyAttribute(_cfgPoseInferRulesExpanded);
                HookManagedConfigEntry(
                    _cfgPoseInferRulesExpanded,
                    "pose-infer-expand-toggle",
                    () => _suppressPoseConfigChangeEvent,
                    value =>
                    {
                        _poseInferRulesExpanded = value;
                        UpdatePoseControlReadOnlyState();
                        return true;
                    },
                    saveValue: null,
                    refreshConfigManager: true);
            }
            _poseInferRulesExpanded = _cfgPoseInferRulesExpanded != null && _cfgPoseInferRulesExpanded.Value;
        }

        private ConfigDescription BuildPoseControlToggleButtonDescription(string openLabel, string closeLabel, int order, bool readOnly)
        {
            var attrs = new ConfigurationManager.ConfigurationManagerAttributes
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

                bool isOpen = boolEntry.Value;
                string buttonText = isOpen ? closeLabel : openLabel;
                bool prevEnabled = GUI.enabled;
                if (attrs.ReadOnly == true)
                {
                    GUI.enabled = false;
                }

                if (GUILayout.Button(buttonText, GUILayout.ExpandWidth(true)))
                {
                    boolEntry.Value = !boolEntry.Value;
                }

                GUI.enabled = prevEnabled;
            };
            return new ConfigDescription(string.Empty, null, attrs);
        }

        private ConfigDescription BuildPoseControlConfigDescription(string description, int order, bool readOnly, bool? browsable = null, bool? isAdvanced = null)
        {
            var attrs = new ConfigurationManager.ConfigurationManagerAttributes
            {
                Order = order,
                ReadOnly = readOnly
            };
            if (browsable.HasValue)
            {
                attrs.Browsable = browsable.Value;
            }
            if (isAdvanced.HasValue)
            {
                attrs.IsAdvanced = isAdvanced.Value;
            }
            return new ConfigDescription(description, null, attrs);
        }

        private void RegisterPoseReadonlyAttribute(ConfigEntryBase entryBase)
        {
            if (entryBase == null)
            {
                return;
            }

            var tags = entryBase.Description != null ? entryBase.Description.Tags : null;
            if (tags == null)
            {
                return;
            }

            foreach (object tag in tags)
            {
                var attr = tag as ConfigurationManager.ConfigurationManagerAttributes;
                if (attr != null)
                {
                    _cfgPoseReadonlyAttributes[entryBase] = attr;
                    break;
                }
            }
        }

        private void UpdatePoseControlReadOnlyState()
        {
            bool showCategories = _poseCategoriesExpanded;
            bool showRules = _poseRulesExpanded && !_poseSimpleModeEnabled;
            bool showInferRules = _poseInferRulesExpanded && !_poseSimpleModeEnabled;

            foreach (var pair in _cfgPoseReadonlyAttributes)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                // グローバルトグルは常に編集可能
                if (_cfgPoseChangeEnabled != null && ReferenceEquals(pair.Key, _cfgPoseChangeEnabled))
                {
                    pair.Value.ReadOnly = false;
                    pair.Value.Browsable = true;
                    continue;
                }
                if (_cfgPoseCategoriesExpanded != null && ReferenceEquals(pair.Key, _cfgPoseCategoriesExpanded))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled;
                    pair.Value.Browsable = true;
                    continue;
                }
                if (_cfgPoseSimpleModeEnabled != null && ReferenceEquals(pair.Key, _cfgPoseSimpleModeEnabled))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled;
                    pair.Value.Browsable = true;
                    continue;
                }
                if (_cfgPoseSimpleModeTriggerKeywords != null && ReferenceEquals(pair.Key, _cfgPoseSimpleModeTriggerKeywords))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled;
                    pair.Value.Browsable = true;
                    continue;
                }
                if (_cfgPoseRulesEnabled != null && ReferenceEquals(pair.Key, _cfgPoseRulesEnabled))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled || _poseSimpleModeEnabled;
                    pair.Value.Browsable = !_poseSimpleModeEnabled;
                    continue;
                }
                if (_cfgPoseRulesExpanded != null && ReferenceEquals(pair.Key, _cfgPoseRulesExpanded))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled || !_poseRulesEnabled || _poseSimpleModeEnabled;
                    pair.Value.Browsable = !_poseSimpleModeEnabled;
                    continue;
                }
                if (_cfgPoseInferRulesEnabled != null && ReferenceEquals(pair.Key, _cfgPoseInferRulesEnabled))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled || _poseSimpleModeEnabled;
                    pair.Value.Browsable = !_poseSimpleModeEnabled;
                    continue;
                }
                if (_cfgPoseInferRulesExpanded != null && ReferenceEquals(pair.Key, _cfgPoseInferRulesExpanded))
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled || !_poseInferRulesEnabled || _poseSimpleModeEnabled;
                    pair.Value.Browsable = !_poseSimpleModeEnabled;
                    continue;
                }

                bool isCategoryEntry = _cfgPoseCategoryEnabledEntries.Values.Any(v => ReferenceEquals(v, pair.Key));
                bool isRuleEntry = _cfgPoseRuleEnabledEntries.Values.Any(v => ReferenceEquals(v, pair.Key));
                bool isInferRuleEntry = _cfgPoseInferRuleEnabledEntries.Values.Any(v => ReferenceEquals(v, pair.Key));
                if (isCategoryEntry)
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled;
                    pair.Value.Browsable = showCategories;
                    continue;
                }
                if (isRuleEntry)
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled || !_poseRulesEnabled || _poseSimpleModeEnabled;
                    pair.Value.Browsable = showRules;
                    continue;
                }
                if (isInferRuleEntry)
                {
                    pair.Value.ReadOnly = !_poseChangeEnabled || !_poseInferRulesEnabled || _poseSimpleModeEnabled;
                    pair.Value.Browsable = showInferRules;
                    continue;
                }

                pair.Value.ReadOnly = !_poseChangeEnabled;
                pair.Value.Browsable = true;
            }
        }

        private void ExpandPoseSectionsOnEnable()
        {
            SetPoseSectionExpanded(ref _poseCategoriesExpanded, _cfgPoseCategoriesExpanded, true);
            SetPoseSectionExpanded(ref _poseRulesExpanded, _cfgPoseRulesExpanded, true);
            SetPoseSectionExpanded(ref _poseInferRulesExpanded, _cfgPoseInferRulesExpanded, true);
        }

        private void SetPoseSectionExpanded(ref bool stateField, ConfigEntry<bool> entry, bool value)
        {
            if (stateField == value && (entry == null || entry.Value == value))
            {
                return;
            }

            bool previousSuppress = _suppressPoseConfigChangeEvent;
            _suppressPoseConfigChangeEvent = true;
            try
            {
                stateField = value;
                if (entry != null)
                {
                    entry.Value = value;
                }
            }
            finally
            {
                _suppressPoseConfigChangeEvent = previousSuppress;
            }
        }

        private void RefreshConfigurationManagerSettingList(string reason)
        {
            try
            {
                if (_configurationManagerType == null)
                {
                    _configurationManagerType = Type.GetType("ConfigurationManager.ConfigurationManager, ConfigurationManager");
                    if (_configurationManagerType == null)
                    {
                        return;
                    }
                }

                if (_configurationManagerBuildSettingListMethod == null)
                {
                    _configurationManagerBuildSettingListMethod =
                        _configurationManagerType.GetMethod("BuildSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_configurationManagerBuildSettingListMethod == null)
                    {
                        return;
                    }
                }

                UnityEngine.Object[] managers = UnityEngine.Object.FindObjectsOfType(_configurationManagerType);
                if (managers == null || managers.Length <= 0)
                {
                    return;
                }

                foreach (UnityEngine.Object manager in managers)
                {
                    _configurationManagerBuildSettingListMethod.Invoke(manager, null);
                }
            }
            catch (Exception ex)
            {
                LogWarn("[pose-cfgui] refresh failed reason=" + reason + " message=" + ex.Message);
            }
        }

        private void SyncPoseControlConfigEntriesFromRuntime()
        {
            _suppressPoseConfigChangeEvent = true;
            try
            {
                if (_cfgPoseChangeEnabled != null)
                {
                    _cfgPoseChangeEnabled.Value = _poseChangeEnabled;
                }
                if (_cfgPoseSimpleModeEnabled != null)
                {
                    _cfgPoseSimpleModeEnabled.Value = _poseSimpleModeEnabled;
                }
                if (_cfgPoseSimpleModeTriggerKeywords != null)
                {
                    _cfgPoseSimpleModeTriggerKeywords.Value = _poseSimpleModeTriggerKeywords;
                }
                if (_cfgPoseRulesEnabled != null)
                {
                    _cfgPoseRulesEnabled.Value = _poseRulesEnabled;
                }
                if (_cfgPoseInferRulesEnabled != null)
                {
                    _cfgPoseInferRulesEnabled.Value = _poseInferRulesEnabled;
                }
                foreach (var pair in _cfgPoseCategoryEnabledEntries)
                {
                    bool enabled = IsPoseCategoryEnabled(pair.Key);
                    if (pair.Value != null)
                    {
                        pair.Value.Value = enabled;
                    }
                }

                foreach (var pair in _cfgPoseRuleEnabledEntries)
                {
                    PoseKeywordScoreRule rule = _poseKeywordScoreRules.FirstOrDefault(r => r != null && string.Equals(r.RuleId, pair.Key, StringComparison.Ordinal));
                    bool enabled = rule == null || rule.Enabled != false;
                    if (pair.Value != null)
                    {
                        pair.Value.Value = enabled;
                    }
                }

                foreach (var pair in _cfgPoseInferRuleEnabledEntries)
                {
                    PoseCategoryInferRule rule = _poseCategoryInferRules.FirstOrDefault(r => r != null && string.Equals(r.RuleId, pair.Key, StringComparison.Ordinal));
                    bool enabled = rule == null || rule.Enabled != false;
                    if (pair.Value != null)
                    {
                        pair.Value.Value = enabled;
                    }
                }
            }
            finally
            {
                _suppressPoseConfigChangeEvent = false;
                UpdatePoseControlReadOnlyState();
            }
        }

        private static string NormalizePoseSimpleModeTriggerKeywords(string csv)
        {
            string source = string.IsNullOrWhiteSpace(csv) ? DefaultPoseSimpleModeTriggerKeywords : csv;
            source = source.Replace('、', ',');
            string[] keywords = SplitKeywords(source);
            if (keywords.Length <= 0)
            {
                keywords = SplitKeywords(DefaultPoseSimpleModeTriggerKeywords);
            }

            return string.Join(",", keywords
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        }

        private string[] GetPoseSimpleModeTriggerKeywords()
        {
            string normalized = NormalizePoseSimpleModeTriggerKeywords(_poseSimpleModeTriggerKeywords);
            if (!string.Equals(_poseSimpleModeTriggerKeywords, normalized, StringComparison.Ordinal))
            {
                _poseSimpleModeTriggerKeywords = normalized;
            }

            return SplitKeywords(normalized);
        }

        private void HookManagedConfigEntry<T>(
            ConfigEntry<T> entry,
            string reason,
            Func<bool> isSuppressed,
            Func<T, bool> applyValue,
            Action<string> saveValue,
            bool refreshConfigManager = false,
            bool restartPipe = false)
        {
            if (entry == null)
            {
                return;
            }

            string entryName = BuildConfigEntryName(entry);
            entry.SettingChanged += (_, __) => OnManagedConfigEntryChanged(
                entryName,
                reason,
                entry.BoxedValue,
                isSuppressed,
                () => applyValue == null || applyValue(entry.Value),
                saveValue,
                refreshConfigManager,
                restartPipe);
        }

        private void OnManagedConfigEntryChanged(
            string entryName,
            string reason,
            object currentValue,
            Func<bool> isSuppressed,
            Func<bool> applyValue,
            Action<string> saveValue,
            bool refreshConfigManager,
            bool restartPipe)
        {
            if (isSuppressed != null && isSuppressed())
            {
                return;
            }

            string safeEntryName = string.IsNullOrWhiteSpace(entryName) ? "(unknown)" : entryName;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "config-manager" : reason;
            string safeValue = currentValue == null ? "(null)" : currentValue.ToString();
            Log("[cfg] SettingChanged entry=" + safeEntryName
                + " reason=" + safeReason
                + " refresh=" + refreshConfigManager
                + " restartPipe=" + restartPipe
                + " value=" + safeValue);

            try
            {
                bool applied = applyValue == null || applyValue();
                if (!applied)
                {
                    Log("[cfg] change skipped entry=" + safeEntryName + " reason=" + safeReason);
                    return;
                }

                saveValue?.Invoke(safeReason);

                if (refreshConfigManager)
                {
                    RefreshConfigurationManagerSettingList(safeReason);
                }

                if (restartPipe)
                {
                    StartOrRestartPipeServer(forceRestart: false, reason: "config_changed:" + safeEntryName);
                }
            }
            catch (Exception ex)
            {
                LogWarn("[cfg] SettingChanged failed entry=" + safeEntryName
                    + " reason=" + safeReason
                    + " message=" + ex.Message);
            }
        }

        private static string BuildConfigEntryName(ConfigEntryBase entry)
        {
            if (entry == null || entry.Definition == null)
            {
                return "(unknown)";
            }

            return entry.Definition.Section + "/" + entry.Definition.Key;
        }

        private void HookConfigEntryEvent<T>(ConfigEntry<T> entry, bool restartPipe)
        {
            HookManagedConfigEntry(
                entry,
                "config-manager",
                () => _suppressConfigChangeEvent,
                _ =>
                {
                    ApplyConfigEntryOverridesToSettings();
                    return true;
                },
                reason =>
                {
                    SaveSettingsToConfigJson(reason);
                    SaveConfigFile(reason: "config-manager-sync");
                    Log("[cfg] change applied and persisted to config.json");
                },
                restartPipe: restartPipe);
        }

        private static PoseKeywordScoreToken CreatePoseScoreToken(string keyword, int score)
        {
            return new PoseKeywordScoreToken
            {
                Keyword = keyword,
                Score = score
            };
        }

        private static PoseKeywordScoreRule CreatePoseScoreRule(
            string ruleId,
            string category,
            int priority,
            string[] poseNames,
            params PoseKeywordScoreToken[] tokens)
        {
            return new PoseKeywordScoreRule
            {
                RuleId = ruleId,
                Category = category,
                Priority = priority,
                PoseNames = poseNames ?? new string[0],
                Tokens = tokens ?? new PoseKeywordScoreToken[0]
            };
        }

        private static PoseCategoryInferRule CreatePoseInferRule(
            string ruleId,
            string targetCategory,
            int priority,
            string[] requiredAll,
            string[] requiredAny,
            string[] excludeAny)
        {
            return new PoseCategoryInferRule
            {
                RuleId = ruleId,
                TargetCategory = targetCategory,
                Priority = priority,
                RequiredAll = requiredAll ?? new string[0],
                RequiredAny = requiredAny ?? new string[0],
                ExcludeAny = excludeAny ?? new string[0]
            };
        }

        private void LoadPoseScoreRules()
        {
            string path = Path.Combine(PluginDir ?? string.Empty, PoseScoreRulesFileName);
            _poseScoreRulesFilePath = path;
            try
            {
                PoseScoreRulesFile file;
                bool wroteBack = false;
                if (!File.Exists(path))
                {
                    file = CreateDefaultPoseScoreRulesFile();
                    SavePoseScoreRulesFile(path, file);
                    wroteBack = true;
                    Log($"[pose-score] default file created: {path}");
                }
                else
                {
                    file = DeserializePoseScoreRulesFile(path);
                    if (TryMigratePoseScoreRulesFile(file))
                    {
                        SavePoseScoreRulesFile(path, file);
                        wroteBack = true;
                        Log($"[pose-score] migrated file updated: {path}");
                    }
                }

                ApplyPoseScoreRulesFile(file, source: path);
                EnsurePoseControlConfigEntries();
                SyncPoseControlConfigEntriesFromRuntime();
                if (wroteBack)
                {
                    Log($"[pose-score] active file saved: {path}");
                }
            }
            catch (Exception ex)
            {
                LogWarn("[pose-score] load failed, fallback to built-in defaults. message=" + ex.Message);
                ApplyPoseScoreRulesFile(CreateDefaultPoseScoreRulesFile(), source: "built-in-default");
                EnsurePoseControlConfigEntries();
                SyncPoseControlConfigEntriesFromRuntime();
            }
        }

        private bool TryMigratePoseScoreRulesFile(PoseScoreRulesFile file)
        {
            if (file == null)
            {
                return false;
            }

            bool changed = false;
            if (file.Version < PoseScoreRulesCurrentVersion)
            {
                file.Version = PoseScoreRulesCurrentVersion;
                changed = true;
            }

            if (!file.Enabled.HasValue)
            {
                file.Enabled = true;
                changed = true;
            }
            if (!file.SimpleModeEnabled.HasValue)
            {
                file.SimpleModeEnabled = false;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(file.SimpleModeTriggerKeywords))
            {
                file.SimpleModeTriggerKeywords = DefaultPoseSimpleModeTriggerKeywords;
                changed = true;
            }
            else
            {
                string normalizedSimpleTriggers = NormalizePoseSimpleModeTriggerKeywords(file.SimpleModeTriggerKeywords);
                if (!string.Equals(file.SimpleModeTriggerKeywords, normalizedSimpleTriggers, StringComparison.Ordinal))
                {
                    file.SimpleModeTriggerKeywords = normalizedSimpleTriggers;
                    changed = true;
                }
            }
            if (!file.RulesEnabled.HasValue)
            {
                file.RulesEnabled = true;
                changed = true;
            }
            if (!file.InferRulesEnabled.HasValue)
            {
                file.InferRulesEnabled = true;
                changed = true;
            }

            if (file.Rules == null)
            {
                file.Rules = new List<PoseKeywordScoreRule>();
                changed = true;
            }

            int customRuleIndex = 0;
            var ruleIdSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoseKeywordScoreRule rule in file.Rules)
            {
                if (rule == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.RuleId))
                {
                    rule.RuleId = "rule_custom_" + customRuleIndex++;
                    changed = true;
                }
                if (!rule.Enabled.HasValue)
                {
                    rule.Enabled = true;
                    changed = true;
                }

                ruleIdSet.Add(rule.RuleId);
            }

            foreach (PoseKeywordScoreRule builtIn in PoseKeywordScoreRules.Where(r => r != null))
            {
                if (string.IsNullOrWhiteSpace(builtIn.RuleId) || ruleIdSet.Contains(builtIn.RuleId))
                {
                    continue;
                }

                file.Rules.Add(ClonePoseScoreRule(builtIn));
                ruleIdSet.Add(builtIn.RuleId);
                changed = true;
            }

            if (file.InferRules == null)
            {
                file.InferRules = new List<PoseCategoryInferRule>();
                changed = true;
            }

            int customInferIndex = 0;
            var inferRuleIdSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoseCategoryInferRule rule in file.InferRules)
            {
                if (rule == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.RuleId))
                {
                    rule.RuleId = "infer_custom_" + customInferIndex++;
                    changed = true;
                }
                if (!rule.Enabled.HasValue)
                {
                    rule.Enabled = true;
                    changed = true;
                }
                inferRuleIdSet.Add(rule.RuleId);
            }

            foreach (PoseCategoryInferRule builtIn in PoseCategoryInferenceRules.Where(r => r != null))
            {
                if (string.IsNullOrWhiteSpace(builtIn.RuleId) || inferRuleIdSet.Contains(builtIn.RuleId))
                {
                    continue;
                }

                file.InferRules.Add(ClonePoseInferRule(builtIn));
                inferRuleIdSet.Add(builtIn.RuleId);
                changed = true;
            }

            if (file.CategoryEnabled == null)
            {
                file.CategoryEnabled = new Dictionary<string, bool>(StringComparer.Ordinal);
                changed = true;
            }

            var allCategories = new HashSet<string>(StringComparer.Ordinal);
            if (file.Rules != null)
            {
                foreach (PoseKeywordScoreRule rule in file.Rules)
                {
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.Category))
                    {
                        allCategories.Add(rule.Category);
                    }
                }
            }
            if (file.InferRules != null)
            {
                foreach (PoseCategoryInferRule rule in file.InferRules)
                {
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.TargetCategory))
                    {
                        allCategories.Add(rule.TargetCategory);
                    }
                }
            }

            foreach (string category in allCategories)
            {
                if (!file.CategoryEnabled.ContainsKey(category))
                {
                    file.CategoryEnabled[category] = true;
                    changed = true;
                }
            }

            return changed;
        }

        private void ApplyPoseScoreRulesFile(PoseScoreRulesFile file, string source)
        {
            if (file == null)
            {
                file = CreateDefaultPoseScoreRulesFile();
                source = "built-in-default(null)";
            }

            _poseChangeEnabled = file.Enabled != false;
            _poseSimpleModeEnabled = file.SimpleModeEnabled == true;
            _poseSimpleModeTriggerKeywords = NormalizePoseSimpleModeTriggerKeywords(file.SimpleModeTriggerKeywords);
            _poseRulesEnabled = file.RulesEnabled != false;
            _poseInferRulesEnabled = file.InferRulesEnabled != false;
            _poseScoreBase = file.ScoreBase > 0 ? file.ScoreBase : DefaultPoseScoreBase;
            _poseAdoptThreshold = file.AdoptThreshold > 0 ? file.AdoptThreshold : DefaultPoseAdoptThreshold;
            _poseForceThreshold = file.ForceThreshold > 0 ? file.ForceThreshold : DefaultPoseForceThreshold;
            if (_poseForceThreshold < _poseAdoptThreshold)
            {
                _poseForceThreshold = _poseAdoptThreshold;
            }

            _categoryAliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (file.CategoryAliases != null)
            {
                foreach (var kv in file.CategoryAliases)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                        _categoryAliases[kv.Key] = kv.Value;
                }
            }

            var normalized = new List<PoseKeywordScoreRule>();
            if (file.Rules != null)
            {
                foreach (PoseKeywordScoreRule rule in file.Rules)
                {
                    if (rule == null || string.IsNullOrWhiteSpace(rule.Category))
                    {
                        continue;
                    }

                    string[] poseNames = (rule.PoseNames ?? new string[0])
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (poseNames.Length <= 0)
                    {
                        continue;
                    }

                    PoseKeywordScoreToken[] tokens = (rule.Tokens ?? new PoseKeywordScoreToken[0])
                        .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Keyword))
                        .Select(t => new PoseKeywordScoreToken
                        {
                            Keyword = t.Keyword,
                            Score = t.Score > 0 ? t.Score : 1
                        })
                        .ToArray();
                    if (tokens.Length <= 0)
                    {
                        continue;
                    }

                    normalized.Add(new PoseKeywordScoreRule
                    {
                        RuleId = string.IsNullOrWhiteSpace(rule.RuleId) ? ("rule_" + normalized.Count) : rule.RuleId,
                        Category = rule.Category,
                        Priority = rule.Priority,
                        PoseNames = poseNames,
                        Tokens = tokens,
                        Enabled = rule.Enabled != false
                    });
                }
            }

            var normalizedInferRules = new List<PoseCategoryInferRule>();
            if (file.InferRules != null)
            {
                foreach (PoseCategoryInferRule rule in file.InferRules)
                {
                    if (rule == null || string.IsNullOrWhiteSpace(rule.TargetCategory))
                    {
                        continue;
                    }

                    string[] requiredAll = (rule.RequiredAll ?? new string[0])
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    string[] requiredAny = (rule.RequiredAny ?? new string[0])
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    string[] excludeAny = (rule.ExcludeAny ?? new string[0])
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                    if (requiredAll.Length <= 0 && requiredAny.Length <= 0)
                    {
                        continue;
                    }

                    normalizedInferRules.Add(new PoseCategoryInferRule
                    {
                        RuleId = string.IsNullOrWhiteSpace(rule.RuleId) ? ("infer_" + normalizedInferRules.Count) : rule.RuleId,
                        TargetCategory = rule.TargetCategory,
                        Priority = rule.Priority,
                        RequiredAll = requiredAll,
                        RequiredAny = requiredAny,
                        ExcludeAny = excludeAny,
                        Enabled = rule.Enabled != false
                    });
                }
            }

            _poseKeywordScoreRules = normalized;
            _poseCategoryInferRules = normalizedInferRules;
            RebuildPoseCategoryEnabledMap(file.CategoryEnabled);
            Log($"[pose-score] loaded rules={_poseKeywordScoreRules.Count} inferRules={_poseCategoryInferRules.Count} simpleMode={_poseSimpleModeEnabled} simpleTriggers='{_poseSimpleModeTriggerKeywords}' rulesEnabled={_poseRulesEnabled} inferEnabled={_poseInferRulesEnabled} scoreBase={_poseScoreBase} adopt={_poseAdoptThreshold} force={_poseForceThreshold} source={source}");
        }

        private void RebuildPoseCategoryEnabledMap(Dictionary<string, bool> fromFile)
        {
            _poseCategoryEnabled.Clear();

            var categorySet = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoseKeywordScoreRule rule in _poseKeywordScoreRules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Category))
                {
                    continue;
                }

                categorySet.Add(rule.Category);
            }

            foreach (PoseCategoryInferRule rule in _poseCategoryInferRules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.TargetCategory))
                {
                    continue;
                }

                categorySet.Add(rule.TargetCategory);
            }

            foreach (string category in categorySet)
            {
                bool enabled = true;
                if (fromFile != null && fromFile.TryGetValue(category, out bool v))
                {
                    enabled = v;
                }

                _poseCategoryEnabled[category] = enabled;
            }
        }

        private PoseScoreRulesFile CreateDefaultPoseScoreRulesFile()
        {
            var rules = PoseKeywordScoreRules
                .Where(r => r != null)
                .Select(ClonePoseScoreRule)
                .ToList();
            var inferRules = PoseCategoryInferenceRules
                .Where(r => r != null)
                .Select(ClonePoseInferRule)
                .ToList();
            var categoryEnabled = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var c in rules.Select(r => r.Category).Concat(inferRules.Select(r => r.TargetCategory)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
            {
                categoryEnabled[c] = true;
            }

            return new PoseScoreRulesFile
            {
                Version = PoseScoreRulesCurrentVersion,
                Enabled = true,
                SimpleModeEnabled = false,
                SimpleModeTriggerKeywords = DefaultPoseSimpleModeTriggerKeywords,
                RulesEnabled = true,
                InferRulesEnabled = true,
                ScoreBase = DefaultPoseScoreBase,
                AdoptThreshold = DefaultPoseAdoptThreshold,
                ForceThreshold = DefaultPoseForceThreshold,
                Rules = rules,
                InferRules = inferRules,
                CategoryEnabled = categoryEnabled
            };
        }

        private static PoseKeywordScoreRule ClonePoseScoreRule(PoseKeywordScoreRule rule)
        {
            if (rule == null)
            {
                return null;
            }

            return new PoseKeywordScoreRule
            {
                RuleId = rule.RuleId,
                Category = rule.Category,
                Priority = rule.Priority,
                Enabled = rule.Enabled != false,
                PoseNames = (rule.PoseNames ?? new string[0]).ToArray(),
                Tokens = (rule.Tokens ?? new PoseKeywordScoreToken[0])
                    .Where(t => t != null)
                    .Select(t => new PoseKeywordScoreToken
                    {
                        Keyword = t.Keyword,
                        Score = t.Score
                    })
                    .ToArray()
            };
        }

        private static PoseCategoryInferRule ClonePoseInferRule(PoseCategoryInferRule rule)
        {
            if (rule == null)
            {
                return null;
            }

            return new PoseCategoryInferRule
            {
                RuleId = rule.RuleId,
                TargetCategory = rule.TargetCategory,
                Priority = rule.Priority,
                Enabled = rule.Enabled != false,
                RequiredAll = (rule.RequiredAll ?? new string[0]).ToArray(),
                RequiredAny = (rule.RequiredAny ?? new string[0]).ToArray(),
                ExcludeAny = (rule.ExcludeAny ?? new string[0]).ToArray()
            };
        }

        private static PoseScoreRulesFile DeserializePoseScoreRulesFile(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("pose_score_rules.json is empty");
            }

            var serializer = new DataContractJsonSerializer(
                typeof(PoseScoreRulesFile),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                PoseScoreRulesFile file = serializer.ReadObject(ms) as PoseScoreRulesFile;
                if (file == null)
                {
                    throw new InvalidDataException("pose_score_rules.json parse returned null");
                }

                return file;
            }
        }

        private static void SavePoseScoreRulesFile(string path, PoseScoreRulesFile file)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var serializer = new DataContractJsonSerializer(
                typeof(PoseScoreRulesFile),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, file);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private void SaveCurrentPoseScoreRulesToFile(string reason)
        {
            string path = _poseScoreRulesFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(PluginDir ?? string.Empty, PoseScoreRulesFileName);
            }

            try
            {
                var file = new PoseScoreRulesFile
                {
                    Version = PoseScoreRulesCurrentVersion,
                    Enabled = _poseChangeEnabled,
                    SimpleModeEnabled = _poseSimpleModeEnabled,
                    SimpleModeTriggerKeywords = _poseSimpleModeTriggerKeywords,
                    RulesEnabled = _poseRulesEnabled,
                    InferRulesEnabled = _poseInferRulesEnabled,
                    ScoreBase = _poseScoreBase,
                    AdoptThreshold = _poseAdoptThreshold,
                    ForceThreshold = _poseForceThreshold,
                    CategoryEnabled = new Dictionary<string, bool>(_poseCategoryEnabled, StringComparer.Ordinal),
                    Rules = (_poseKeywordScoreRules ?? new List<PoseKeywordScoreRule>())
                        .Where(r => r != null)
                        .Select(ClonePoseScoreRule)
                        .ToList(),
                    InferRules = (_poseCategoryInferRules ?? new List<PoseCategoryInferRule>())
                        .Where(r => r != null)
                        .Select(ClonePoseInferRule)
                        .ToList()
                };

                SavePoseScoreRulesFile(path, file);
                Log("[pose-score] saved by " + reason);
            }
            catch (Exception ex)
            {
                LogWarn("[pose-score] save failed reason=" + reason + " message=" + ex.Message);
            }
        }

        private void EnsurePoseClassificationFilesFromProc(HSceneProc proc)
        {
            if (proc == null || string.IsNullOrWhiteSpace(PluginDir))
            {
                return;
            }

            EnsurePoseListFileFromProc(proc);

            bool created = false;
            created |= EnsureSinglePoseClassificationFile(proc, PoseSonyuClassifiedFileName, classifyType: "sonyu");
            created |= EnsureSinglePoseClassificationFile(proc, PoseHoushiClassifiedFileName, classifyType: "houshi");
            created |= EnsureSinglePoseClassificationFile(proc, PoseMasturbationClassifiedFileName, classifyType: "masturbation");

            if (created)
            {
                LoadPoseCategoryEntries();
            }
        }

        private bool EnsurePoseListFileFromProc(HSceneProc proc)
        {
            if (proc == null || string.IsNullOrWhiteSpace(PluginDir))
            {
                return false;
            }

            string path = Path.Combine(PluginDir, PoseListFileName);
            if (File.Exists(path))
            {
                return false;
            }

            try
            {
                var lists = LstUseAnimInfoField?.GetValue(proc) as List<HSceneProc.AnimationListInfo>[];
                if (lists == null)
                {
                    LogWarn("[pose-list] auto-create skipped (lstUseAnimInfo is null)");
                    return false;
                }

                List<PoseListItem> poses = BuildPoseListItems(lists);
                SavePoseList(path, poses);
                Log($"[pose-list] auto-created: {path} entries={poses.Count}");
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[pose-list] auto-create failed: " + ex.Message);
                return false;
            }
        }

        private bool EnsureSinglePoseClassificationFile(HSceneProc proc, string fileName, string classifyType)
        {
            if (proc == null || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(PluginDir) || string.IsNullOrWhiteSpace(classifyType))
            {
                return false;
            }

            string path = Path.Combine(PluginDir, fileName);
            if (File.Exists(path))
            {
                return false;
            }

            try
            {
                var lists = LstUseAnimInfoField?.GetValue(proc) as List<HSceneProc.AnimationListInfo>[];
                if (lists == null)
                {
                    LogWarn("[pose-classify] auto-create skipped (lstUseAnimInfo is null): " + fileName);
                    return false;
                }

                Dictionary<string, List<PoseClassificationItem>> categories;
                if (string.Equals(classifyType, "sonyu", StringComparison.Ordinal))
                {
                    categories = BuildAutoSonyuPoseCategories(lists);
                }
                else if (string.Equals(classifyType, "houshi", StringComparison.Ordinal))
                {
                    categories = BuildAutoHoushiPoseCategories(lists);
                }
                else if (string.Equals(classifyType, "masturbation", StringComparison.Ordinal))
                {
                    categories = BuildAutoMasturbationPoseCategories(lists);
                }
                else
                {
                    LogWarn("[pose-classify] auto-create skipped (unknown classifyType): " + classifyType);
                    return false;
                }

                SavePoseClassification(path, categories);
                int entryCount = categories.Sum(x => x.Value != null ? x.Value.Count : 0);
                Log($"[pose-classify] auto-created: {path} categories={categories.Count} entries={entryCount}");
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[pose-classify] auto-create failed file=" + fileName + " message=" + ex.Message);
                return false;
            }
        }

        private static Dictionary<string, List<PoseClassificationItem>> BuildAutoSonyuPoseCategories(List<HSceneProc.AnimationListInfo>[] lists)
        {
            var categories = CreateCategoryMap(SonyuCategoryNames);
            if (lists == null)
            {
                return categories;
            }

            for (int mode = 0; mode < lists.Length; mode++)
            {
                if (!IsSonyuMode(mode))
                {
                    continue;
                }

                var list = lists[mode];
                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var info = list[i];
                    if (info == null || string.IsNullOrWhiteSpace(info.nameAnimation))
                    {
                        continue;
                    }

                    string category = ClassifySonyuPoseCategory(info.nameAnimation);
                    AddPoseClassificationItem(categories, category, info.nameAnimation, mode);
                }
            }

            return categories;
        }

        private static List<PoseListItem> BuildPoseListItems(List<HSceneProc.AnimationListInfo>[] lists)
        {
            var results = new List<PoseListItem>();
            if (lists == null)
            {
                return results;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int mode = 0; mode < lists.Length; mode++)
            {
                var list = lists[mode];
                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var info = list[i];
                    if (info == null || string.IsNullOrWhiteSpace(info.nameAnimation))
                    {
                        continue;
                    }

                    string key = mode + "|" + info.id + "|" + info.nameAnimation;
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    results.Add(new PoseListItem
                    {
                        Id = info.id,
                        Mode = info.mode.ToString(),
                        ModeInt = mode,
                        NameAnimation = info.nameAnimation
                    });
                }
            }

            return results
                .OrderBy(x => x.ModeInt)
                .ThenBy(x => x.Id)
                .ThenBy(x => x.NameAnimation, StringComparer.Ordinal)
                .ToList();
        }

        private static Dictionary<string, List<PoseClassificationItem>> BuildAutoHoushiPoseCategories(List<HSceneProc.AnimationListInfo>[] lists)
        {
            var categories = CreateCategoryMap(HoushiCategoryNames);
            if (lists == null)
            {
                return categories;
            }

            for (int mode = 0; mode < lists.Length; mode++)
            {
                if (!IsHoushiMode(mode))
                {
                    continue;
                }

                var list = lists[mode];
                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var info = list[i];
                    if (info == null || string.IsNullOrWhiteSpace(info.nameAnimation))
                    {
                        continue;
                    }

                    string category = ClassifyHoushiPoseCategory(info.nameAnimation);
                    AddPoseClassificationItem(categories, category, info.nameAnimation, mode);
                }
            }

            return categories;
        }

        private static Dictionary<string, List<PoseClassificationItem>> BuildAutoMasturbationPoseCategories(List<HSceneProc.AnimationListInfo>[] lists)
        {
            var categories = CreateCategoryMap(MasturbationCategoryNames);
            if (lists == null)
            {
                return categories;
            }

            const string categoryName = "オナニー系";
            for (int mode = 0; mode < lists.Length; mode++)
            {
                if (!IsMasturbationMode(mode))
                {
                    continue;
                }

                var list = lists[mode];
                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var info = list[i];
                    if (info == null || string.IsNullOrWhiteSpace(info.nameAnimation))
                    {
                        continue;
                    }

                    AddPoseClassificationItem(categories, categoryName, info.nameAnimation, mode);
                }
            }

            return categories;
        }

        private static Dictionary<string, List<PoseClassificationItem>> CreateCategoryMap(string[] categoryNames)
        {
            var map = new Dictionary<string, List<PoseClassificationItem>>(StringComparer.Ordinal);
            if (categoryNames == null)
            {
                return map;
            }

            for (int i = 0; i < categoryNames.Length; i++)
            {
                string category = categoryNames[i];
                if (string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                if (!map.ContainsKey(category))
                {
                    map[category] = new List<PoseClassificationItem>();
                }
            }

            return map;
        }

        private static bool IsSonyuMode(int mode)
        {
            return mode == 2 || mode == 7 || mode == 9;
        }

        private static bool IsHoushiMode(int mode)
        {
            return mode == 1 || mode == 6 || mode == 8;
        }

        private static bool IsMasturbationMode(int mode)
        {
            return mode == 3;
        }

        private static string ClassifySonyuPoseCategory(string poseName)
        {
            string name = poseName ?? string.Empty;

            if (ContainsAnyCategoryKeyword(name, "逆騎乗", "背面騎乗", "後ろ向き", "Reverse Cowgirl"))
            {
                return "背面騎乗位系";
            }
            if (ContainsAnyCategoryKeyword(name, "騎乗", "Cowgirl", "またが", "跨"))
            {
                return "騎乗位系";
            }
            if (ContainsAnyCategoryKeyword(name, "側位", "横", "Side", "Princess Hug"))
            {
                return "測位系";
            }
            if (ContainsAnyCategoryKeyword(name, "座位", "椅子", "床", "正座", "膝立て", "Sitting"))
            {
                return "座位系";
            }

            bool standing = ContainsAnyCategoryKeyword(name, "立ち", "立位", "Standing", "駅弁", "Wall");
            bool back = ContainsAnyCategoryKeyword(name, "バック", "後背", "後ろ", "doggy", "Doggystyle", "from behind", "フェンス");
            if (standing && back)
            {
                return "立後背位系";
            }
            if (back)
            {
                return "後背位系";
            }
            if (standing)
            {
                return "立位系";
            }

            return "正常位系";
        }

        private static string ClassifyHoushiPoseCategory(string poseName)
        {
            string name = poseName ?? string.Empty;

            if (ContainsAnyCategoryKeyword(name, "69", "シックスナイン", "sixty"))
            {
                return "69系";
            }
            if (ContainsAnyCategoryKeyword(name, "フェラ", "口", "oral", "blow", "咥", "しゃぶ"))
            {
                return "フェラ系";
            }
            if (ContainsAnyCategoryKeyword(name, "パイズリ", "boob", "titty", "乳"))
            {
                return "パイズリ系";
            }
            if (ContainsAnyCategoryKeyword(name, "クンニ", "cunni", "舐"))
            {
                return "クンニ系";
            }
            if (ContainsAnyCategoryKeyword(name, "顔面騎乗", "face sit"))
            {
                return "顔面騎乗系";
            }
            if (ContainsAnyCategoryKeyword(name, "足コキ", "leg", "foot"))
            {
                return "足コキ系";
            }
            if (ContainsAnyCategoryKeyword(name, "手コキ", "hand", "手"))
            {
                return "手コキ系";
            }

            return "キス・愛撫系";
        }

        private static bool ContainsAnyCategoryKeyword(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length <= 0)
            {
                return false;
            }

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (ContainsKeyword(text, keyword))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddPoseClassificationItem(
            Dictionary<string, List<PoseClassificationItem>> categories,
            string category,
            string nameAnimation,
            int modeInt)
        {
            if (categories == null || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(nameAnimation))
            {
                return;
            }

            if (!categories.TryGetValue(category, out var list))
            {
                list = new List<PoseClassificationItem>();
                categories[category] = list;
            }

            bool exists = list.Any(x =>
                x != null &&
                x.ModeInt == modeInt &&
                string.Equals(x.NameAnimation, nameAnimation, StringComparison.Ordinal));
            if (exists)
            {
                return;
            }

            list.Add(new PoseClassificationItem
            {
                NameAnimation = nameAnimation,
                ModeInt = modeInt
            });
        }

        private static void SavePoseClassification(string path, Dictionary<string, List<PoseClassificationItem>> categories)
        {
            var root = new PoseClassificationFile
            {
                Categories = categories ?? new Dictionary<string, List<PoseClassificationItem>>(StringComparer.Ordinal)
            };

            var serializer = new DataContractJsonSerializer(
                typeof(PoseClassificationFile),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, root);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private static void SavePoseList(string path, List<PoseListItem> poses)
        {
            var root = new PoseListFile
            {
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Source = "HSceneProc.lstUseAnimInfo",
                Poses = poses ?? new List<PoseListItem>()
            };

            var serializer = new DataContractJsonSerializer(
                typeof(PoseListFile),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, root);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private void LoadPoseCategoryEntries()
        {
            _poseEntriesByCategory.Clear();
            _poseNameAliasesByCanonical.Clear();
            int added = 0;
            added += AppendPoseCategoriesFromFile(PoseSonyuClassifiedFileName);
            added += AppendPoseCategoriesFromFile(PoseHoushiClassifiedFileName);
            added += AppendPoseCategoriesFromFile(PoseMasturbationClassifiedFileName);
            int aliasCount = LoadPoseNameAliasesFromTranslatedFile();
            Log($"[pose-classify] loaded categories={_poseEntriesByCategory.Count} entries={added} aliasKeys={_poseNameAliasesByCanonical.Count} aliases={aliasCount}");
        }

        private int LoadPoseNameAliasesFromTranslatedFile()
        {
            if (string.IsNullOrWhiteSpace(PluginDir))
            {
                return 0;
            }

            string path = Path.Combine(PluginDir, PoseListTranslatedFileName);
            if (!File.Exists(path))
            {
                LogWarn("[pose-alias] file not found: " + path);
                return 0;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    LogWarn("[pose-alias] file empty: " + path);
                    return 0;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(PoseTranslatedListFile),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                PoseTranslatedListFile root;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    root = serializer.ReadObject(ms) as PoseTranslatedListFile;
                }

                if (root?.Poses == null || root.Poses.Count <= 0)
                {
                    LogWarn("[pose-alias] poses empty: " + path);
                    return 0;
                }

                int added = 0;
                foreach (PoseTranslatedListItem pose in root.Poses)
                {
                    if (pose == null)
                    {
                        continue;
                    }

                    string canonical = (pose.NameAnimationOriginal ?? string.Empty).Trim();
                    string alias = (pose.NameAnimation ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    if (string.Equals(canonical, alias, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!_poseNameAliasesByCanonical.TryGetValue(canonical, out List<string> aliases))
                    {
                        aliases = new List<string>();
                        _poseNameAliasesByCanonical[canonical] = aliases;
                    }

                    if (aliases.Any(x => string.Equals(x, alias, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    aliases.Add(alias);
                    added++;
                }

                return added;
            }
            catch (Exception ex)
            {
                LogWarn("[pose-alias] load failed: " + ex.Message);
                return 0;
            }
        }

        private int AppendPoseCategoriesFromFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(PluginDir) || string.IsNullOrWhiteSpace(fileName))
            {
                return 0;
            }

            string path = Path.Combine(PluginDir, fileName);
            if (!File.Exists(path))
            {
                LogWarn("[pose-classify] file not found: " + path);
                return 0;
            }

            try
            {
                PoseClassificationFile root = DeserializePoseClassification(path);
                if (root == null)
                {
                    LogWarn("[pose-classify] root parse null: " + fileName);
                    return 0;
                }

                if (root.Categories == null)
                {
                    LogWarn("[pose-classify] categories parse null: " + fileName);
                    return 0;
                }

                if (root.Categories.Count <= 0)
                {
                    LogWarn("[pose-classify] categories empty: " + fileName);
                    return 0;
                }

                int added = 0;
                foreach (var pair in root.Categories)
                {
                    string category = pair.Key;
                    if (string.IsNullOrWhiteSpace(category) || pair.Value == null)
                    {
                        continue;
                    }

                    if (!_poseEntriesByCategory.TryGetValue(category, out var list))
                    {
                        list = new List<PoseCategoryEntry>();
                        _poseEntriesByCategory[category] = list;
                    }

                    foreach (var item in pair.Value)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.NameAnimation))
                        {
                            continue;
                        }

                        bool exists = list.Any(x =>
                            x != null &&
                            x.ModeInt == item.ModeInt &&
                            string.Equals(x.NameAnimation, item.NameAnimation, StringComparison.Ordinal));
                        if (exists)
                        {
                            continue;
                        }

                        list.Add(new PoseCategoryEntry
                        {
                            NameAnimation = item.NameAnimation,
                            ModeInt = item.ModeInt
                        });
                        added++;
                    }
                }

                return added;
            }
            catch (Exception ex)
            {
                LogWarn("[pose-classify] load failed file=" + fileName + " message=" + ex.Message);
                return 0;
            }
        }

        private static PoseClassificationFile DeserializePoseClassification(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(
                typeof(PoseClassificationFile),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as PoseClassificationFile;
            }
        }


        private static string BuildPoseEntryKey(PoseCategoryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.NameAnimation))
            {
                return string.Empty;
            }

            return $"{entry.ModeInt}|{entry.NameAnimation}";
        }

        private static string BuildPoseRuleEntryLabel(PoseKeywordScoreRule rule, int index)
        {
            string category = rule != null && !string.IsNullOrWhiteSpace(rule.Category) ? rule.Category : "未分類";
            string[] tokens = (rule?.Tokens ?? new PoseKeywordScoreToken[0])
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Keyword))
                .Select(t => t.Keyword)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            string hint = tokens.Length > 0
                ? string.Join("+", tokens)
                : (rule?.RuleId ?? "rule");
            hint = TruncateForConfigLabel(hint, 16);
            return $"{index:D2} {category}:{hint}";
        }

        private static string BuildPoseInferRuleEntryLabel(PoseCategoryInferRule rule, int index)
        {
            string category = rule != null && !string.IsNullOrWhiteSpace(rule.TargetCategory) ? rule.TargetCategory : "未分類";
            string hint = ExtractInferRuleHint(rule);
            hint = TruncateForConfigLabel(hint, 18);
            return $"{index:D2} {category}:{hint}";
        }

        private static string ExtractInferRuleHint(PoseCategoryInferRule rule)
        {
            string[] reqAll = rule?.RequiredAll ?? new string[0];
            string[] reqAny = rule?.RequiredAny ?? new string[0];
            if (reqAll.Length > 0 && reqAny.Length > 0)
            {
                return reqAll[0] + "+" + reqAny[0];
            }
            if (reqAll.Length > 0)
            {
                return reqAll[0];
            }
            if (reqAny.Length > 0)
            {
                return reqAny[0];
            }
            return rule?.RuleId ?? "infer";
        }

        private static string TruncateForConfigLabel(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (maxLength <= 0 || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "…";
        }

        private string[] ResolvePoseCategoryAliases(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return new string[0];
            }

            var aliases = new List<string>();
            if (_categoryAliases.TryGetValue(category, out var mapped) && mapped != null)
            {
                aliases.AddRange(mapped.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            aliases.Add(category);
            if (category.EndsWith("系", StringComparison.Ordinal) && category.Length > 1)
            {
                aliases.Add(category.Substring(0, category.Length - 1));
            }

            return aliases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(x => x.Length)
                .ToArray();
        }

        private bool IsReloadKeyDown()
        {
            KeyboardShortcut reloadKey = ResolveReloadKey();
            return Bindings.Down("MainGameVoiceFaceEventBridge.reload", reloadKey.ToString());
        }

        private void BindConfigEntries()
        {
            InitializeComposerMotionIntensityConfig();
            _cfgReceiveTimingEnabled = Config.Bind("Diagnostics", "ReceiveTimingEnabled", false,
                "受信処理時間をメモリに記録し、受信が2秒途切れてからreceive-timingへCSV出力する。");
            _cfgEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "プラグイン機能を有効化する。");

            _cfgVerboseLog = Config.Bind(
                "General",
                "VerboseLog",
                false,
                "詳細ログ（Info）を有効化する。OFF時はWarning/Errorのみ出力。");

            _cfgReloadKey = Config.Bind(
                "Input",
                "ReloadKey",
                new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl),
                "設定再読込キー。");

            _cfgStateDumpKey = Config.Bind(
                "Input",
                "StateDumpKey",
                new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl, KeyCode.LeftShift),
                "状態ダンプを専用ログへ1回だけ出すキー。");

            _cfgPlaybackVolume = Config.Bind(
                "Audio",
                "PlaybackVolume",
                1.0f,
                new ConfigDescription(
                    "FemalePlaybackVolumeが-1のときに使う外部音声の音量（0.0 - 1.0）。変更は再生中にも反映。",
                    new AcceptableValueRange<float>(0f, 1f)));

            _cfgFemalePlaybackVolume = Config.Bind(
                "Audio",
                "FemalePlaybackVolume",
                1.0f,
                new ConfigDescription(
                    "女の子の読み上げ音量。スライダーで0〜500%に調整。100%が通常音量。再生中の変更も反映。",
                    new AcceptableValueRange<float>(-1f, PluginSettings.MaxFemalePlaybackVolume),
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        DispName = "女の子の音量",
                        CustomDrawer = entry => FemaleVoiceVolumeSlider.Draw(entry, _cfgPlaybackVolume.Value)
                    }));

            _cfgExternalPlaybackPitch = Config.Bind(
                "Audio",
                "ExternalPlaybackPitch",
                1.0f,
                new ConfigDescription(
                    "外部音声再生ピッチ（0.1 - 3.0）。",
                    new AcceptableValueRange<float>(0.1f, 3f)));

            const string cdSec = "ClothesDetection";
            _cfgTopKeywords      = Config.Bind(cdSec, "TopKeywords",       "上着,ジャケット,トップス",                          "トップス系ワード（カンマ区切り）");
            _cfgBottomKeywords   = Config.Bind(cdSec, "BottomKeywords",    "スカート,ホットパンツ,ミニスカ,ボトムス,ズボン,パンツ", "ボトムス系ワード");
            _cfgBraKeywords      = Config.Bind(cdSec, "BraKeywords",       "ブラ",                                               "ブラ系ワード");
            _cfgShortsKeywords   = Config.Bind(cdSec, "ShortsKeywords",    "パンティー,パンティ",                                 "パンティ系ワード");
            _cfgGlovesKeywords   = Config.Bind(cdSec, "GlovesKeywords",    "グローブ,手袋",                                      "手袋系ワード");
            _cfgPanthoseKeywords = Config.Bind(cdSec, "PanthoseKeywords",  "ガーターベルト,パンスト,ガーター",                    "パンスト系ワード");
            _cfgSocksKeywords    = Config.Bind(cdSec, "SocksKeywords",     "ストッキング,ニーハイ,靴下",                          "靴下系ワード");
            _cfgShoesKeywords    = Config.Bind(cdSec, "ShoesKeywords",     "ハイヒール,スニーカー,サンダル,ヒール,靴",            "靴系ワード");
            _cfgGlassesKeywords  = Config.Bind(cdSec, "GlassesKeywords",   "メガネ,眼鏡,めがね",                                   "眼鏡系ワード");
            _cfgRemoveKeywords   = Config.Bind(cdSec, "RemoveKeywords",    "脱ぐね,脱いじゃう",                                  "脱衣トリガーワード");
            _cfgShiftKeywords    = Config.Bind(cdSec, "ShiftKeywords",     "ずらすね,半脱ぎにするね",                             "ずらしトリガーワード");
            _cfgShiftSecondKeywords = Config.Bind(cdSec, "ShiftSecondKeywords", "下げるね",                                      "2段目ずらしトリガーワード");
            _cfgPutOnKeywords    = Config.Bind(cdSec, "PutOnKeywords",     "着るね,付けるね",                                    "着用トリガーワード");
            _cfgRemoveAllKeywords= Config.Bind(cdSec, "RemoveAllKeywords", "全裸になるね,全部脱ぐね,全部脱いじゃう",              "全脱ぎトリガーワード");
            _cfgPutOnAllKeywords = Config.Bind(cdSec, "PutOnAllKeywords",  "全部着るね",                                         "全着用トリガーワード");
            _cfgCoordPattern     = Config.Bind(cdSec, "CoordPattern",      "に着替えるね",                                       "着替えトリガーパターン");
            _cfgNamedPoseTriggerSuffix = Config.Bind(
                "NamedPose",
                "TriggerSuffix",
                "のポーズになるね",
                "固有名ポーズを呼び出す女ボイス文末。例: 腕組み立ちのポーズになるね");
            _cfgCameraTriggerKeywords = Config.Bind(cdSec, "CameraTriggerKeywords", "カメラにして,カメラ切り替えて,視点にして,視点切り替えて,アングルにして,で見せて", "カメラ切替トリガーワード");
            _cfgEnableVideoPlaybackByResponseText = Config.Bind(
                "VideoPlayback",
                "EnableVideoPlaybackByResponseText",
                true,
                "response_text解析で動画再生（\"流す\"など）を有効化する。");
            _cfgVideoPlaybackTriggerKeywords = Config.Bind(
                "VideoPlayback",
                "VideoPlaybackTriggerKeywords",
                "流す",
                "動画再生トリガーワード（カンマ区切り）。");
            _cfgAstralModeTriggerKeywords = Config.Bind(
                "AstralMode",
                "AstralModeTriggerKeywords",
                "アストラル",
                "アストラル界突入トリガーワード（カンマ区切り）。アストラル中は無視。");
            _cfgRealWorldTriggerKeywords = Config.Bind(
                "AstralMode",
                "RealWorldTriggerKeywords",
                "現実",
                "現実復帰トリガーワード（カンマ区切り）。現実中は無視。");
            _cfgSequenceSubtitleEnabled = Config.Bind(
                "SequenceSubtitle",
                "Enabled",
                true,
                "speak_sequence再生時に字幕をSubtitleEventBridgeへ送信する。");
            _cfgSequenceSubtitleHost = Config.Bind(
                "SequenceSubtitle",
                "Host",
                "127.0.0.1",
                "SubtitleEventBridgeのHTTPホスト。");
            _cfgSequenceSubtitlePort = Config.Bind(
                "SequenceSubtitle",
                "Port",
                18766,
                new ConfigDescription(
                    "SubtitleEventBridgeのHTTPポート。",
                    new AcceptableValueRange<int>(1, 65535)));
            _cfgSequenceSubtitleEndpointPath = Config.Bind(
                "SequenceSubtitle",
                "EndpointPath",
                "/subtitle-event",
                "SubtitleEventBridgeのエンドポイント。");
            _cfgSequenceSubtitleBackend = Config.Bind(
                "SequenceSubtitle",
                "Backend",
                "Overlay",
                new ConfigDescription(
                    "送信する字幕backend。HシーンではOverlayを使用する。",
                    new AcceptableValueList<string>("Auto", "InformationUI", "Overlay")));
            _cfgSequenceSubtitleDisplayMode = Config.Bind(
                "SequenceSubtitle",
                "DisplayMode",
                "StackFemale",
                "送信する字幕display_mode。");
            _cfgSequenceSubtitleSendMode = Config.Bind(
                "SequenceSubtitle",
                "SendMode",
                "PerLine",
                new ConfigDescription(
                    "sequence字幕の送信方式。PerLine=行ごと、FullTextOnce=全文を最初に1回送信。",
                    new AcceptableValueList<string>("PerLine", "FullTextOnce")));
            _cfgSequenceSubtitleHoldPaddingSeconds = Config.Bind(
                "SequenceSubtitle",
                "HoldPaddingSeconds",
                0.2f,
                new ConfigDescription(
                    "各行字幕の音声長に足す保持秒数。",
                    new AcceptableValueRange<float>(0f, 5f)));
            _cfgSequenceSubtitleProgressPrefixEnabled = Config.Bind(
                "SequenceSubtitle",
                "ProgressPrefixEnabled",
                true,
                "複数行のspeak_sequence字幕にインデックス表示を付ける。");
            _cfgEnableFacePresetApply = Config.Bind(
                "FacePreset",
                "EnableFacePresetApply",
                true,
                "受信したfacePreset指定（name/id/random）で表情プリセットを適用する。");
            _cfgFacePresetJsonRelativePath = Config.Bind(
                "FacePreset",
                "FacePresetJsonRelativePath",
                DefaultFacePresetJsonRelativePath,
                "表情プリセットJSONの相対パス（DLL配置フォルダ基準）。");
            _cfgEnableSubCameraPresetForward = Config.Bind(
                "Camera",
                "EnableSubCameraPresetForward",
                Settings.EnableSubCameraPresetForward,
                "カメラトリガーワード検知時、MainGameSubCameraDisplayProbe にも独立にプリセット呼び出しを転送する。");
            _cfgEnableMorphBridgeTriggers = Config.Bind(
                "CharacterMorph",
                "EnableMorphBridgeTriggers",
                Settings.EnableMorphBridgeTriggers,
                "response_text解析でMainGameCharacterMorphBridgeの変身/戻るトリガーを有効化する。");
            _cfgMorphBridgeResetKeywords = Config.Bind(
                "CharacterMorph",
                "ResetKeywords",
                Settings.MorphBridgeResetKeywords,
                "元に戻すトリガーワード（カンマ区切り）。例: 元に戻るね。");
            _cfgMorphBridgeTransitionSeconds = Config.Bind(
                "CharacterMorph",
                "TransitionSeconds",
                Settings.MorphBridgeTransitionSeconds,
                new ConfigDescription(
                    "変身/戻るにかける秒数。",
                    new AcceptableValueRange<float>(0f, 60f)));

            SetupMorphCardEntries();

            BindScenarioTextConfigEntries();

            RegisterConfigEntryEvents();
        }

        private void RegisterConfigEntryEvents()
        {
            HookConfigEntryEvent(_cfgEnabled, restartPipe: true);
            HookConfigEntryEvent(_cfgVerboseLog, restartPipe: false);
            HookConfigEntryEvent(_cfgReceiveTimingEnabled, restartPipe: false);
            HookConfigEntryEvent(_cfgReloadKey, restartPipe: false);
            HookConfigEntryEvent(_cfgStateDumpKey, restartPipe: false);
            HookConfigEntryEvent(_cfgPlaybackVolume, restartPipe: false);
            HookConfigEntryEvent(_cfgFemalePlaybackVolume, restartPipe: false);
            HookConfigEntryEvent(_cfgExternalPlaybackPitch, restartPipe: false);
            HookConfigEntryEvent(_cfgTopKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgBottomKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgBraKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgShortsKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgGlovesKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgPanthoseKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgSocksKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgShoesKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgGlassesKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgRemoveKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgShiftKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgShiftSecondKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgPutOnKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgRemoveAllKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgPutOnAllKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgCoordPattern, restartPipe: false);
            HookConfigEntryEvent(_cfgNamedPoseTriggerSuffix, restartPipe: false);
            HookConfigEntryEvent(_cfgCameraTriggerKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgEnableVideoPlaybackByResponseText, restartPipe: false);
            HookConfigEntryEvent(_cfgVideoPlaybackTriggerKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgAstralModeTriggerKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgRealWorldTriggerKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleEnabled, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleHost, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitlePort, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleEndpointPath, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleBackend, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleDisplayMode, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleSendMode, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleHoldPaddingSeconds, restartPipe: false);
            HookConfigEntryEvent(_cfgSequenceSubtitleProgressPrefixEnabled, restartPipe: false);
            HookConfigEntryEvent(_cfgEnableFacePresetApply, restartPipe: false);
            HookConfigEntryEvent(_cfgFacePresetJsonRelativePath, restartPipe: false);
            HookConfigEntryEvent(_cfgEnableSubCameraPresetForward, restartPipe: false);
            HookConfigEntryEvent(_cfgEnableMorphBridgeTriggers, restartPipe: false);
            HookConfigEntryEvent(_cfgMorphBridgeTransformKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgMorphBridgeResetKeywords, restartPipe: false);
            HookConfigEntryEvent(_cfgMorphBridgeTransitionSeconds, restartPipe: false);
            RegisterScenarioTextConfigEntryEvents();
        }

        private void ApplySettingsToConfigEntries(PluginSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _suppressConfigChangeEvent = true;
            try
            {
                if (_cfgEnabled != null) _cfgEnabled.Value = settings.Enabled;
                if (_cfgVerboseLog != null) _cfgVerboseLog.Value = settings.VerboseLog;
                if (_cfgReceiveTimingEnabled != null) _cfgReceiveTimingEnabled.Value = settings.ReceiveTimingEnabled;
                if (_cfgReloadKey != null) _cfgReloadKey.Value = ParseKeyboardShortcut(settings.ReloadKey, _cfgReloadKey.Value);
                if (_cfgStateDumpKey != null) _cfgStateDumpKey.Value = ParseKeyboardShortcut(settings.StateDumpKey, _cfgStateDumpKey.Value);
                if (_cfgPlaybackVolume != null) _cfgPlaybackVolume.Value = Mathf.Clamp01(settings.PlaybackVolume);
                if (_cfgFemalePlaybackVolume != null) _cfgFemalePlaybackVolume.Value = Mathf.Clamp(settings.FemalePlaybackVolume, -1f, PluginSettings.MaxFemalePlaybackVolume);
                if (_cfgExternalPlaybackPitch != null) _cfgExternalPlaybackPitch.Value = Mathf.Clamp(settings.ExternalPlaybackPitch, 0.1f, 3f);

                if (_cfgTopKeywords != null) _cfgTopKeywords.Value = settings.TopKeywords ?? _cfgTopKeywords.Value;
                if (_cfgBottomKeywords != null) _cfgBottomKeywords.Value = settings.BottomKeywords ?? _cfgBottomKeywords.Value;
                if (_cfgBraKeywords != null) _cfgBraKeywords.Value = settings.BraKeywords ?? _cfgBraKeywords.Value;
                if (_cfgShortsKeywords != null) _cfgShortsKeywords.Value = settings.ShortsKeywords ?? _cfgShortsKeywords.Value;
                if (_cfgGlovesKeywords != null) _cfgGlovesKeywords.Value = settings.GlovesKeywords ?? _cfgGlovesKeywords.Value;
                if (_cfgPanthoseKeywords != null) _cfgPanthoseKeywords.Value = settings.PanthoseKeywords ?? _cfgPanthoseKeywords.Value;
                if (_cfgSocksKeywords != null) _cfgSocksKeywords.Value = settings.SocksKeywords ?? _cfgSocksKeywords.Value;
                if (_cfgShoesKeywords != null) _cfgShoesKeywords.Value = settings.ShoesKeywords ?? _cfgShoesKeywords.Value;
                if (_cfgGlassesKeywords != null) _cfgGlassesKeywords.Value = settings.GlassesKeywords ?? _cfgGlassesKeywords.Value;
                if (_cfgRemoveKeywords != null) _cfgRemoveKeywords.Value = settings.RemoveKeywords ?? _cfgRemoveKeywords.Value;
                if (_cfgShiftKeywords != null) _cfgShiftKeywords.Value = settings.ShiftKeywords ?? _cfgShiftKeywords.Value;
                if (_cfgShiftSecondKeywords != null) _cfgShiftSecondKeywords.Value = settings.ShiftSecondKeywords ?? _cfgShiftSecondKeywords.Value;
                if (_cfgPutOnKeywords != null) _cfgPutOnKeywords.Value = settings.PutOnKeywords ?? _cfgPutOnKeywords.Value;
                if (_cfgRemoveAllKeywords != null) _cfgRemoveAllKeywords.Value = settings.RemoveAllKeywords ?? _cfgRemoveAllKeywords.Value;
                if (_cfgPutOnAllKeywords != null) _cfgPutOnAllKeywords.Value = settings.PutOnAllKeywords ?? _cfgPutOnAllKeywords.Value;
                if (_cfgCoordPattern != null) _cfgCoordPattern.Value = settings.CoordPattern ?? _cfgCoordPattern.Value;
                if (_cfgCameraTriggerKeywords != null) _cfgCameraTriggerKeywords.Value = string.IsNullOrWhiteSpace(settings.CameraTriggerKeywords) ? "カメラにして,カメラ切り替えて,視点にして,視点切り替えて,アングルにして,で見せて" : settings.CameraTriggerKeywords.Trim();
                if (_cfgEnableVideoPlaybackByResponseText != null) _cfgEnableVideoPlaybackByResponseText.Value = settings.EnableVideoPlaybackByResponseText;
                if (_cfgVideoPlaybackTriggerKeywords != null)
                {
                    _cfgVideoPlaybackTriggerKeywords.Value = string.IsNullOrWhiteSpace(settings.VideoPlaybackTriggerKeywords)
                        ? "流す"
                        : settings.VideoPlaybackTriggerKeywords.Trim();
                }
                if (_cfgAstralModeTriggerKeywords != null)
                {
                    _cfgAstralModeTriggerKeywords.Value = string.IsNullOrWhiteSpace(settings.AstralModeTriggerKeywords)
                        ? "アストラル"
                        : settings.AstralModeTriggerKeywords.Trim();
                }
                if (_cfgRealWorldTriggerKeywords != null)
                {
                    _cfgRealWorldTriggerKeywords.Value = string.IsNullOrWhiteSpace(settings.RealWorldTriggerKeywords)
                        ? "現実"
                        : settings.RealWorldTriggerKeywords.Trim();
                }
                if (_cfgSequenceSubtitleEnabled != null) _cfgSequenceSubtitleEnabled.Value = settings.SequenceSubtitleEnabled;
                if (_cfgSequenceSubtitleHost != null) _cfgSequenceSubtitleHost.Value = string.IsNullOrWhiteSpace(settings.SequenceSubtitleHost) ? "127.0.0.1" : settings.SequenceSubtitleHost.Trim();
                if (_cfgSequenceSubtitlePort != null) _cfgSequenceSubtitlePort.Value = Mathf.Clamp(settings.SequenceSubtitlePort, 1, 65535);
                if (_cfgSequenceSubtitleEndpointPath != null) _cfgSequenceSubtitleEndpointPath.Value = string.IsNullOrWhiteSpace(settings.SequenceSubtitleEndpointPath) ? "/subtitle-event" : settings.SequenceSubtitleEndpointPath.Trim();
                if (_cfgSequenceSubtitleBackend != null) _cfgSequenceSubtitleBackend.Value = string.IsNullOrWhiteSpace(settings.SequenceSubtitleBackend) ? "Overlay" : settings.SequenceSubtitleBackend.Trim();
                if (_cfgSequenceSubtitleDisplayMode != null) _cfgSequenceSubtitleDisplayMode.Value = string.IsNullOrWhiteSpace(settings.SequenceSubtitleDisplayMode) ? "StackFemale" : settings.SequenceSubtitleDisplayMode.Trim();
                if (_cfgSequenceSubtitleSendMode != null) _cfgSequenceSubtitleSendMode.Value = NormalizeSequenceSubtitleSendMode(settings.SequenceSubtitleSendMode);
                if (_cfgSequenceSubtitleHoldPaddingSeconds != null) _cfgSequenceSubtitleHoldPaddingSeconds.Value = Mathf.Clamp(settings.SequenceSubtitleHoldPaddingSeconds, 0f, 5f);
                if (_cfgSequenceSubtitleProgressPrefixEnabled != null) _cfgSequenceSubtitleProgressPrefixEnabled.Value = settings.SequenceSubtitleProgressPrefixEnabled;
                if (_cfgEnableFacePresetApply != null) _cfgEnableFacePresetApply.Value = settings.EnableFacePresetApply;
                if (_cfgFacePresetJsonRelativePath != null)
                {
                    _cfgFacePresetJsonRelativePath.Value = string.IsNullOrWhiteSpace(settings.FacePresetJsonRelativePath)
                        ? DefaultFacePresetJsonRelativePath
                        : settings.FacePresetJsonRelativePath.Trim();
                }

                if (_cfgEnableSubCameraPresetForward != null) _cfgEnableSubCameraPresetForward.Value = settings.EnableSubCameraPresetForward;
                if (_cfgEnableMorphBridgeTriggers != null) _cfgEnableMorphBridgeTriggers.Value = settings.EnableMorphBridgeTriggers;
                if (_cfgMorphBridgeTransformKeywords != null) _cfgMorphBridgeTransformKeywords.Value = string.IsNullOrWhiteSpace(settings.MorphBridgeTransformKeywords) ? "変身するね" : settings.MorphBridgeTransformKeywords.Trim();
                if (_cfgMorphBridgeResetKeywords != null) _cfgMorphBridgeResetKeywords.Value = string.IsNullOrWhiteSpace(settings.MorphBridgeResetKeywords) ? "元に戻るね" : settings.MorphBridgeResetKeywords.Trim();
                if (_cfgMorphBridgeTransitionSeconds != null) _cfgMorphBridgeTransitionSeconds.Value = Mathf.Clamp(settings.MorphBridgeTransitionSeconds, 0f, 60f);

                ApplyScenarioTextSettingsToConfigEntries(settings);
            }
            finally
            {
                _suppressConfigChangeEvent = false;
            }
        }

        private void ApplyConfigEntryOverridesToSettings()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.Enabled = _cfgEnabled != null ? _cfgEnabled.Value : Settings.Enabled;
            Settings.VerboseLog = _cfgVerboseLog != null ? _cfgVerboseLog.Value : Settings.VerboseLog;
            Settings.ReceiveTimingEnabled = _cfgReceiveTimingEnabled != null ? _cfgReceiveTimingEnabled.Value : Settings.ReceiveTimingEnabled;
            Settings.ReloadKey = ResolveReloadKey().ToString();
            Settings.StateDumpKey = ResolveStateDumpKey().ToString();
            Settings.PlaybackVolume = _cfgPlaybackVolume != null ? Mathf.Clamp01(_cfgPlaybackVolume.Value) : Settings.PlaybackVolume;
            Settings.FemalePlaybackVolume = _cfgFemalePlaybackVolume != null
                ? Mathf.Clamp(_cfgFemalePlaybackVolume.Value, -1f, PluginSettings.MaxFemalePlaybackVolume)
                : Settings.FemalePlaybackVolume;
            Settings.ExternalPlaybackPitch = _cfgExternalPlaybackPitch != null
                ? Mathf.Clamp(_cfgExternalPlaybackPitch.Value, 0.1f, 3f)
                : Settings.ExternalPlaybackPitch;

            if (_cfgTopKeywords != null) Settings.TopKeywords = _cfgTopKeywords.Value;
            if (_cfgBottomKeywords != null) Settings.BottomKeywords = _cfgBottomKeywords.Value;
            if (_cfgBraKeywords != null) Settings.BraKeywords = _cfgBraKeywords.Value;
            if (_cfgShortsKeywords != null) Settings.ShortsKeywords = _cfgShortsKeywords.Value;
            if (_cfgGlovesKeywords != null) Settings.GlovesKeywords = _cfgGlovesKeywords.Value;
            if (_cfgPanthoseKeywords != null) Settings.PanthoseKeywords = _cfgPanthoseKeywords.Value;
            if (_cfgSocksKeywords != null) Settings.SocksKeywords = _cfgSocksKeywords.Value;
            if (_cfgShoesKeywords != null) Settings.ShoesKeywords = _cfgShoesKeywords.Value;
            if (_cfgGlassesKeywords != null) Settings.GlassesKeywords = _cfgGlassesKeywords.Value;
            if (_cfgRemoveKeywords != null) Settings.RemoveKeywords = _cfgRemoveKeywords.Value;
            if (_cfgShiftKeywords != null) Settings.ShiftKeywords = _cfgShiftKeywords.Value;
            if (_cfgShiftSecondKeywords != null) Settings.ShiftSecondKeywords = _cfgShiftSecondKeywords.Value;
            if (_cfgPutOnKeywords != null) Settings.PutOnKeywords = _cfgPutOnKeywords.Value;
            if (_cfgRemoveAllKeywords != null) Settings.RemoveAllKeywords = _cfgRemoveAllKeywords.Value;
            if (_cfgPutOnAllKeywords != null) Settings.PutOnAllKeywords = _cfgPutOnAllKeywords.Value;
            if (_cfgCoordPattern != null) Settings.CoordPattern = _cfgCoordPattern.Value;
            if (_cfgCameraTriggerKeywords != null) Settings.CameraTriggerKeywords = _cfgCameraTriggerKeywords.Value;
            if (_cfgEnableVideoPlaybackByResponseText != null) Settings.EnableVideoPlaybackByResponseText = _cfgEnableVideoPlaybackByResponseText.Value;
            if (_cfgVideoPlaybackTriggerKeywords != null)
            {
                Settings.VideoPlaybackTriggerKeywords = string.IsNullOrWhiteSpace(_cfgVideoPlaybackTriggerKeywords.Value)
                    ? "流す"
                    : _cfgVideoPlaybackTriggerKeywords.Value.Trim();
            }
            if (_cfgAstralModeTriggerKeywords != null)
            {
                Settings.AstralModeTriggerKeywords = string.IsNullOrWhiteSpace(_cfgAstralModeTriggerKeywords.Value)
                    ? "アストラル"
                    : _cfgAstralModeTriggerKeywords.Value.Trim();
            }
            if (_cfgRealWorldTriggerKeywords != null)
            {
                Settings.RealWorldTriggerKeywords = string.IsNullOrWhiteSpace(_cfgRealWorldTriggerKeywords.Value)
                    ? "現実"
                    : _cfgRealWorldTriggerKeywords.Value.Trim();
            }
            if (_cfgSequenceSubtitleEnabled != null) Settings.SequenceSubtitleEnabled = _cfgSequenceSubtitleEnabled.Value;
            if (_cfgSequenceSubtitleHost != null) Settings.SequenceSubtitleHost = string.IsNullOrWhiteSpace(_cfgSequenceSubtitleHost.Value) ? "127.0.0.1" : _cfgSequenceSubtitleHost.Value.Trim();
            if (_cfgSequenceSubtitlePort != null) Settings.SequenceSubtitlePort = Mathf.Clamp(_cfgSequenceSubtitlePort.Value, 1, 65535);
            if (_cfgSequenceSubtitleEndpointPath != null) Settings.SequenceSubtitleEndpointPath = string.IsNullOrWhiteSpace(_cfgSequenceSubtitleEndpointPath.Value) ? "/subtitle-event" : _cfgSequenceSubtitleEndpointPath.Value.Trim();
            if (_cfgSequenceSubtitleBackend != null) Settings.SequenceSubtitleBackend = string.IsNullOrWhiteSpace(_cfgSequenceSubtitleBackend.Value) ? "Overlay" : _cfgSequenceSubtitleBackend.Value.Trim();
            if (_cfgSequenceSubtitleDisplayMode != null) Settings.SequenceSubtitleDisplayMode = string.IsNullOrWhiteSpace(_cfgSequenceSubtitleDisplayMode.Value) ? "StackFemale" : _cfgSequenceSubtitleDisplayMode.Value.Trim();
            if (_cfgSequenceSubtitleSendMode != null) Settings.SequenceSubtitleSendMode = NormalizeSequenceSubtitleSendMode(_cfgSequenceSubtitleSendMode.Value);
            if (_cfgSequenceSubtitleHoldPaddingSeconds != null) Settings.SequenceSubtitleHoldPaddingSeconds = Mathf.Clamp(_cfgSequenceSubtitleHoldPaddingSeconds.Value, 0f, 5f);
            if (_cfgSequenceSubtitleProgressPrefixEnabled != null) Settings.SequenceSubtitleProgressPrefixEnabled = _cfgSequenceSubtitleProgressPrefixEnabled.Value;
            if (_cfgEnableFacePresetApply != null) Settings.EnableFacePresetApply = _cfgEnableFacePresetApply.Value;
            if (_cfgEnableSubCameraPresetForward != null) Settings.EnableSubCameraPresetForward = _cfgEnableSubCameraPresetForward.Value;
            if (_cfgEnableMorphBridgeTriggers != null) Settings.EnableMorphBridgeTriggers = _cfgEnableMorphBridgeTriggers.Value;
            if (_cfgMorphBridgeTransformKeywords != null)
            {
                Settings.MorphBridgeTransformKeywords = string.IsNullOrWhiteSpace(_cfgMorphBridgeTransformKeywords.Value)
                    ? "変身するね"
                    : _cfgMorphBridgeTransformKeywords.Value.Trim();
            }
            if (_cfgMorphBridgeResetKeywords != null)
            {
                Settings.MorphBridgeResetKeywords = string.IsNullOrWhiteSpace(_cfgMorphBridgeResetKeywords.Value)
                    ? "元に戻るね"
                    : _cfgMorphBridgeResetKeywords.Value.Trim();
            }
            if (_cfgMorphBridgeTransitionSeconds != null)
            {
                Settings.MorphBridgeTransitionSeconds = Mathf.Clamp(_cfgMorphBridgeTransitionSeconds.Value, 0f, 60f);
            }
            if (_cfgFacePresetJsonRelativePath != null)
            {
                Settings.FacePresetJsonRelativePath = string.IsNullOrWhiteSpace(_cfgFacePresetJsonRelativePath.Value)
                    ? DefaultFacePresetJsonRelativePath
                    : _cfgFacePresetJsonRelativePath.Value.Trim();
            }

            ApplyScenarioTextConfigEntriesToSettings();

            Settings.Normalize();
        }

        private void SaveSettingsToConfigJson(string reason)
        {
            if (Settings == null || string.IsNullOrWhiteSpace(PluginDir))
            {
                return;
            }

            try
            {
                SettingsStore.SaveAll(PluginDir, Settings);
            }
            catch (Exception ex)
            {
                LogWarn("[settings] save failed reason=" + reason + " message=" + ex.Message);
            }
        }

        private static KeyboardShortcut ParseKeyboardShortcut(string value, KeyboardShortcut fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string[] tokens = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length <= 0 || !TryParseKeyCode(tokens[0], out var main))
            {
                return fallback;
            }

            if (tokens.Length == 1)
            {
                return new KeyboardShortcut(main);
            }

            var modifiers = new List<KeyCode>();
            for (int i = 1; i < tokens.Length; i++)
            {
                if (TryParseKeyCode(tokens[i], out var modifier))
                {
                    modifiers.Add(modifier);
                }
            }

            return modifiers.Count > 0
                ? new KeyboardShortcut(main, modifiers.ToArray())
                : new KeyboardShortcut(main);
        }

        private static bool TryParseKeyCode(string token, out KeyCode keyCode)
        {
            return Enum.TryParse((token ?? string.Empty).Trim(), true, out keyCode);
        }

        private KeyboardShortcut ResolveReloadKey()
        {
            if (_cfgReloadKey != null)
            {
                return _cfgReloadKey.Value;
            }

            return new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl);
        }

        private KeyboardShortcut ResolveStateDumpKey()
        {
            if (_cfgStateDumpKey != null)
            {
                return _cfgStateDumpKey.Value;
            }

            return new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl, KeyCode.LeftShift);
        }

        private bool IsStateDumpKeyDown()
        {
            KeyboardShortcut dumpKey = ResolveStateDumpKey();
            return Bindings.Down("MainGameVoiceFaceEventBridge.dump", dumpKey.ToString());
        }

        private void ReloadSettings()
        {
            string oldPipe = Settings?.PipeName ?? string.Empty;
            Settings = SettingsStore.LoadOrCreate(PluginDir, Log, LogWarn, LogError);
            LoadScenarioTextRules();
            ApplySettingsToConfigEntries(Settings);
            SaveConfigFile(reason: "reload");
            LoadPoseScoreRules();
            LoadPoseCategoryEntries();
            LogGuardSettings("reload");

            bool pipeChanged = !string.Equals(
                oldPipe,
                Settings?.PipeName ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            StartOrRestartPipeServer(forceRestart: pipeChanged, reason: pipeChanged ? "reload:pipe_changed" : "reload");
            Log("settings reloaded by Ctrl+R");
        }

        private void SaveConfigFile(string reason)
        {
            try
            {
                Config.Save();
            }
            catch (Exception ex)
            {
                LogWarn("[cfg] save failed reason=" + reason + " message=" + ex.Message);
            }
        }
    }
}
