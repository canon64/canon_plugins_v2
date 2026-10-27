using System;
using System.IO;

namespace MainGameBlankMapAdd.Bootstrap
{
    internal sealed class PluginPaths
    {
        private PluginPaths(string pluginDirectory)
        {
            PluginDirectory = pluginDirectory;
            LogDirectory = Path.Combine(pluginDirectory, "_logs");
            LogFile = Path.Combine(LogDirectory, "info.txt");
            SettingsFile = Path.Combine(pluginDirectory, "MapAddSettings.json");
            PresetDirectory = Path.Combine(pluginDirectory, "MapAddPresets");
            RoomProfilesFile = Path.Combine(pluginDirectory, "RoomLayoutProfiles.json");
            WebCamDevicesFile = Path.Combine(pluginDirectory, "webcam_devices.txt");
            MonoCacheDirectory = Path.Combine(pluginDirectory, "_mono_cache");
        }

        internal string PluginDirectory { get; }
        internal string LogDirectory { get; }
        internal string LogFile { get; }
        internal string SettingsFile { get; }
        internal string PresetDirectory { get; }
        internal string RoomProfilesFile { get; }
        internal string WebCamDevicesFile { get; }
        internal string MonoCacheDirectory { get; }

        internal static PluginPaths Create(string pluginLocation, string fallbackPluginDirectory)
        {
            string directory = string.IsNullOrWhiteSpace(pluginLocation)
                ? null
                : Path.GetDirectoryName(pluginLocation);
            if (string.IsNullOrWhiteSpace(directory))
                directory = fallbackPluginDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Plugin directory could not be resolved.");
            return new PluginPaths(Path.GetFullPath(directory));
        }
    }
}
