using System;
using System.Reflection;
using HarmonyLib;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private bool _ksPlugTimeLookupDone;
        private FieldInfo _ksPlugTimePassesInHField;
        private bool _ksPlugTimeSuppressed;
        private bool _ksPlugTimeOriginalValue;

        private void EnsureKsPlugTimeApi()
        {
            if (_ksPlugTimeLookupDone) return;
            _ksPlugTimeLookupDone = true;

            try
            {
                Type settingsType = AccessTools.TypeByName("ksPlug.Settings");
                if (settingsType == null)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        settingsType = asm.GetType("ksPlug.Settings", throwOnError: false);
                        if (settingsType != null) break;
                    }
                }

                if (settingsType == null)
                {
                    SimpleFileLogger.Info("ksPlugTime: ksPlug.Settings not found; time suppression disabled");
                    return;
                }

                _ksPlugTimePassesInHField = AccessTools.Field(settingsType, "TimePassesInH");
                if (_ksPlugTimePassesInHField == null || _ksPlugTimePassesInHField.FieldType != typeof(bool))
                {
                    SimpleFileLogger.Warn($"ksPlugTime: TimePassesInH field not found or not bool on {settingsType.FullName}");
                    _ksPlugTimePassesInHField = null;
                    return;
                }

                SimpleFileLogger.Info($"ksPlugTime: reflected {settingsType.FullName}.TimePassesInH");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("ksPlugTime: reflection lookup failed: " + ex.Message);
                _ksPlugTimePassesInHField = null;
            }
        }

        private void SetKsPlugTimePassesSuppressed(bool suppress, string phase)
        {
            if (_cfgSuppressKsPlugTimePassesInAstral == null || !_cfgSuppressKsPlugTimePassesInAstral.Value) return;

            EnsureKsPlugTimeApi();
            if (_ksPlugTimePassesInHField == null) return;

            try
            {
                if (suppress)
                {
                    if (!_ksPlugTimeSuppressed)
                    {
                        _ksPlugTimeOriginalValue = (bool)_ksPlugTimePassesInHField.GetValue(null);
                        _ksPlugTimeSuppressed = true;
                        SimpleFileLogger.Info($"ksPlugTime: suppress start phase={phase} original={_ksPlugTimeOriginalValue}");
                    }

                    if ((bool)_ksPlugTimePassesInHField.GetValue(null))
                        _ksPlugTimePassesInHField.SetValue(null, false);
                }
                else if (_ksPlugTimeSuppressed)
                {
                    _ksPlugTimePassesInHField.SetValue(null, _ksPlugTimeOriginalValue);
                    SimpleFileLogger.Info($"ksPlugTime: suppress end phase={phase} restored={_ksPlugTimeOriginalValue}");
                    _ksPlugTimeSuppressed = false;
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"ksPlugTime: suppress failed phase={phase} suppress={suppress}: {ex.Message}");
            }
        }

        private void MaintainKsPlugTimePassesSuppression()
        {
            if (!_ksPlugTimeSuppressed) return;
            SetKsPlugTimePassesSuppressed(true, "maintain");
        }
    }
}
