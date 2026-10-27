using System;

namespace MainGirlHipHijack.Services
{
    internal sealed class SettingsCoordinator
    {
        private const float SaveDelaySeconds = 0.25f;

        private readonly string _pluginDirectory;
        private readonly Action<string> _logWarning;
        private readonly Func<float> _getTime;
        private bool _savePending;
        private float _saveDueTime;

        private SettingsCoordinator(
            string pluginDirectory,
            BodyIkGizmoSettings settings,
            Action<string> logWarning,
            Func<float> getTime)
        {
            _pluginDirectory = pluginDirectory ?? string.Empty;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logWarning = logWarning;
            _getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
        }

        internal BodyIkGizmoSettings Settings { get; }

        internal static SettingsCoordinator Load(
            string pluginDirectory,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logError,
            Func<float> getTime)
        {
            BodyIkGizmoSettings settings = SettingsStore.LoadOrCreate(
                pluginDirectory,
                logInfo,
                logWarning,
                logError);
            return new SettingsCoordinator(pluginDirectory, settings, logWarning, getTime);
        }

        internal void RequestSave(bool immediate = false)
        {
            if (immediate)
            {
                _savePending = false;
                SettingsStore.Save(_pluginDirectory, Settings, _logWarning);
                return;
            }

            _savePending = true;
            _saveDueTime = _getTime() + SaveDelaySeconds;
        }

        internal bool FlushIfDue()
        {
            if (!_savePending || _getTime() < _saveDueTime)
                return false;

            _savePending = false;
            SettingsStore.Save(_pluginDirectory, Settings, _logWarning);
            return true;
        }
    }
}
