using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        public static bool BeginAstralModeSuppression()
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            inst.BeginAstralModeSuppressionInternal();
            return true;
        }

        public static bool EndAstralModeSuppression()
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            inst.EndAstralModeSuppressionInternal();
            return true;
        }

        private void BeginAstralModeSuppressionInternal()
        {
            _astralModeSuppressed = true;
            ClearNativeLightsOverride();

            for (int i = 0; i < _lightEntries.Count; i++)
            {
                LightEntry entry = _lightEntries[i];
                if (entry == null) continue;
                if (entry.Go != null) entry.Go.SetActive(false);
                if (entry.Light != null) entry.Light.enabled = false;
            }

            _log.Info("[AstralMode] club lights suppressed; native lights restored to cached defaults");
        }

        private void EndAstralModeSuppressionInternal()
        {
            _astralModeSuppressed = false;

            for (int i = 0; i < _lightEntries.Count; i++)
            {
                LightEntry entry = _lightEntries[i];
                if (entry == null) continue;
                bool enabled = entry.Settings == null || entry.Settings.Enabled;
                if (entry.Go != null) entry.Go.SetActive(enabled);
                if (entry.Light != null) entry.Light.enabled = enabled;
            }

            if (_settings != null && _settings.NativeLight != null && _settings.NativeLight.OverrideEnabled)
                ApplyNativeLightOverride();

            _log.Info("[AstralMode] club lights restored");
        }
    }
}
