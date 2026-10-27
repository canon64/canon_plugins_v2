using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public partial class Plugin
    {
        private ConfigEntry<bool> _impactBoostEnabled;
        private ConfigEntry<bool> _impactMoanModeEnabled;
        private Type _impactVrType;
        private PropertyInfo _impactVrActiveProperty;
        private float _nextImpactRefreshTime;
        private float _nextImpactEntryResolveTime;
        private float _nextImpactVrTypeSearchTime;
        private ConfigEntry<bool> _impactNormalEnabledEntry;
        private ConfigEntry<bool> _impactVrEnabledEntry;
        private ConfigEntry<bool> _impactNormalMoanEntry;
        private ConfigEntry<bool> _impactVrMoanEntry;
        private Action<bool> _applyNormalImpactEnabled;
        private Action<bool> _applyVrImpactEnabled;
        private readonly bool _impactVrProcess = Process.GetCurrentProcess().ProcessName
            .IndexOf("_VR", StringComparison.OrdinalIgnoreCase) >= 0;

        private void UpdateImpactBoost()
        {
            float now = Time.unscaledTime;
            if (now < _nextImpactRefreshTime) return;
            _nextImpactRefreshTime = now + 0.1f;
            ResolveImpactEntriesIfNeeded(now);

            bool vr = IsImpactBoostVr();
            if (_impactBoostEnabled == null)
            {
                // Update runs after plugin initialization; migrate the selected mode's current setting.
                bool initial = (vr ? _impactVrEnabledEntry : _impactNormalEnabledEntry)?.Value ?? false;
                _impactBoostEnabled = Config.Bind("03.Video", "ImpactBoostEnabled", initial,
                    "再生バーのImpactBoost。ON: 通常/VRの対応側だけ有効。OFF: 両方無効。");
                _impactMoanModeEnabled = Config.Bind("03.Video", "ImpactMoanModeEnabled", false,
                    "喘ぎ声モード。ImpactBoost ON時、選択した音源の音声をセリフの有無に関係なく挿入。");
            }
            SetImpactEntry(_impactNormalMoanEntry, _impactMoanModeEnabled.Value);
            SetImpactEntry(_impactVrMoanEntry, _impactMoanModeEnabled.Value);
            if (_applyNormalImpactEnabled == null)
            {
                _applyNormalImpactEnabled = enabled => SetImpactEntry(_impactNormalEnabledEntry, enabled);
                _applyVrImpactEnabled = enabled => SetImpactEntry(_impactVrEnabledEntry, enabled);
            }
            ImpactBoostRouting.Apply(_impactBoostEnabled.Value, vr,
                _applyNormalImpactEnabled,
                _applyVrImpactEnabled);
        }

        private void ResolveImpactEntriesIfNeeded(float now)
        {
            if (now < _nextImpactEntryResolveTime) return;
            _nextImpactEntryResolveTime = now + 1f;
            if (_impactNormalEnabledEntry == null)
                _impactNormalEnabledEntry = GetImpactEntry("com.kks.main.voiceimpactboost", "00.General");
            if (_impactVrEnabledEntry == null)
                _impactVrEnabledEntry = GetImpactEntry("com.kks.main.vrhipikvoiceimpactboost", "1.全般");
            if (_impactNormalMoanEntry == null)
                _impactNormalMoanEntry = GetImpactEntry("com.kks.main.voiceimpactboost", "00.General", "MoanModeEnabled");
            if (_impactVrMoanEntry == null)
                _impactVrMoanEntry = GetImpactEntry("com.kks.main.vrhipikvoiceimpactboost", "1.全般", "MoanModeEnabled");
        }

        private bool IsImpactBoostVr()
        {
            if (_impactVrProcess) return true;
            if (_impactVrType == null && Time.unscaledTime >= _nextImpactVrTypeSearchTime)
            {
                _nextImpactVrTypeSearchTime = Time.unscaledTime + 5f;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _impactVrType = assembly.GetType("VRGIN.Core.VR", false);
                    if (_impactVrType != null) break;
                }
                if (_impactVrType != null)
                    _impactVrActiveProperty = _impactVrType.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
            }
            return _impactVrActiveProperty?.GetValue(null, null) is bool active && active;
        }

        private static ConfigEntry<bool> GetImpactEntry(string guid, string section, string key = "Enabled")
        {
            if (!Chainloader.PluginInfos.TryGetValue(guid, out var plugin) || plugin.Instance == null)
                return null;
            return plugin.Instance.Config.TryGetEntry<bool>(section, key, out var entry) ? entry : null;
        }

        private static void SetImpactEntry(ConfigEntry<bool> entry, bool value)
        {
            if (entry != null && entry.Value != value) entry.Value = value;
        }
    }
}
