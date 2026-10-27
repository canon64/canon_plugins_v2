using System;
using MainGameBlankMapAdd.Bootstrap;

namespace MainGameBlankMapAdd.Settings
{
    internal sealed class SettingsRepository
    {
        private readonly PluginPaths _paths;
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private readonly Action<string> _error;

        internal SettingsRepository(
            PluginPaths paths,
            Action<string> info,
            Action<string> warning,
            Action<string> error)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
            _error = error ?? throw new ArgumentNullException(nameof(error));
        }

        internal PluginSettings LoadCurrent()
        {
            return SettingsStore.LoadOrCreate(_paths.PluginDirectory, _info, _warning, _error);
        }

        internal bool TryLoadFromPath(string path, out PluginSettings settings, out string error)
        {
            return SettingsStore.TryLoadFromPath(path, out settings, out error);
        }

        internal void SaveCurrent(PluginSettings settings)
        {
            SettingsStore.Save(_paths.SettingsFile, settings);
        }

        internal void SaveToPath(string path, PluginSettings settings)
        {
            SettingsStore.Save(path, settings);
        }
    }
}
