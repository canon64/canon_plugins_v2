using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameHandySync
{
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(PluginSettings));

        internal static PluginSettings Load(string pluginDir)
        {
            string path = Path.Combine(pluginDir, "HandySyncSettings.json");
            if (!File.Exists(path))
                return new PluginSettings();

            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var settings = (PluginSettings)Serializer.ReadObject(fs);
                    return settings ?? new PluginSettings();
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Failed to load settings: {ex.Message}");
                return new PluginSettings();
            }
        }

        internal static void Save(PluginSettings settings, string pluginDir)
        {
            string path = Path.Combine(pluginDir, "HandySyncSettings.json");
            try
            {
                using (var fs = File.Create(path))
                {
                    Serializer.WriteObject(fs, settings);
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
