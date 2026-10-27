using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private void ToggleForceVanillaSpeed()
        {
            var s = Settings;
            if (s == null)
                return;

            bool enable = !s.ForceVanillaSpeed;
            string notice = enable
                ? "Vanilla speed lock ON"
                : "Vanilla speed lock OFF";
            SetForceVanillaSpeed(enable, enable ? "force vanilla on" : "force vanilla off", saveSettings: true, notice: notice);
        }

        private void RestoreVanillaSpeedBaseline()
        {
            var s = Settings;
            if (s == null)
                return;

            if (_bpmMeasure.Running)
            {
                StopBpmMeasure("restore vanilla baseline", keepResult: true);
            }

            float sourceMin = s.SourceMinSpeed;
            float sourceMax = Mathf.Max(sourceMin + 0.0001f, s.SourceMaxSpeed);

            s.TargetMinSpeed = sourceMin;
            s.TargetMaxSpeed = sourceMax;

            float appliedMin = Mathf.Max(0f, EstimateBpmFromMappedSpeed(sourceMin));
            float appliedMax = Mathf.Max(appliedMin + 0.0001f, EstimateBpmFromMappedSpeed(sourceMax));
            s.AppliedBpmMin = appliedMin;
            s.AppliedBpmMax = appliedMax;

            SetForceVanillaSpeed(true, "restore vanilla speed", saveSettings: false, notice: null);
            SaveSettings("restore vanilla speed");
            SyncUiFromSettings();
            _appliedBpmMinInput = appliedMin.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            _appliedBpmMaxInput = appliedMax.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            LogInfo(
                $"vanilla baseline restored: target=[{s.TargetMinSpeed:0.###}/{s.TargetMaxSpeed:0.###}] " +
                $"applied=[{s.AppliedBpmMin:0.##}/{s.AppliedBpmMax:0.##}]");
            ShowUiNotice("Vanilla baseline restored");
        }

        private void DisableForceVanillaForCustomApply(string reason)
        {
            var s = Settings;
            if (s == null || !s.ForceVanillaSpeed)
                return;

            s.ForceVanillaSpeed = false;
            PushForceVanillaToConfigEntry(false);
            LogInfo("force vanilla speed auto-off: " + reason);
        }

        private void SetForceVanillaSpeed(bool enable, string reason, bool saveSettings, string notice)
        {
            var s = Settings;
            if (s == null)
                return;

            bool changed = s.ForceVanillaSpeed != enable;
            s.ForceVanillaSpeed = enable;
            PushForceVanillaToConfigEntry(enable);

            if (enable)
            {
                ResetVideoCueRuntime(clearTriggerOnce: true);
            }

            if (saveSettings && changed)
            {
                SaveSettings(reason);
            }

            if (changed)
            {
                LogInfo($"force vanilla speed: {enable}");
            }

            if (!string.IsNullOrWhiteSpace(notice))
            {
                ShowUiNotice(notice);
            }
        }

        private void PushForceVanillaToConfigEntry(bool enable)
        {
            if (_cfgForceVanillaSpeed == null)
                return;

            if (_cfgForceVanillaSpeed.Value == enable)
                return;

            _suppressConfigSync = true;
            _cfgForceVanillaSpeed.Value = enable;
            _suppressConfigSync = false;
        }
    }
}
