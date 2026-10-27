using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameDollMode
{
    internal static class SettingsStore
    {
        internal const string SettingsFileName = "config.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static PluginSettings LoadOrCreate(
            string pluginDir,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            if (string.IsNullOrWhiteSpace(pluginDir))
            {
                return CreateDefault();
            }

            string path = Path.Combine(pluginDir, SettingsFileName);
            try
            {
                Directory.CreateDirectory(pluginDir);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("[settings] create dir failed: " + ex.Message);
            }

            if (!File.Exists(path))
            {
                var created = CreateDefault();
                created.Normalize();
                Save(path, created, logWarn);
                logInfo?.Invoke("[settings] created default config");
                return created;
            }

            try
            {
                string json = File.ReadAllText(path, Utf8NoBom);
                var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
                using (var ms = new MemoryStream(Utf8NoBom.GetBytes(json)))
                {
                    var parsed = serializer.ReadObject(ms) as PluginSettings;
                    if (parsed == null)
                    {
                        throw new InvalidOperationException("parsed settings is null");
                    }

                    parsed.Normalize();
                    Save(path, parsed, logWarn);
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                logError?.Invoke("[settings] load failed: " + ex.Message);
                var fallback = CreateDefault();
                fallback.Normalize();
                Save(path, fallback, logWarn);
                return fallback;
            }
        }

        internal static void SaveToDefault(string pluginDir, PluginSettings settings, Action<string> logWarn)
        {
            if (string.IsNullOrWhiteSpace(pluginDir) || settings == null)
            {
                return;
            }

            string path = Path.Combine(pluginDir, SettingsFileName);
            Save(path, settings, logWarn);
        }

        private static void Save(string path, PluginSettings settings, Action<string> logWarn)
        {
            try
            {
                settings.Normalize();
                var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, settings);
                    string json = Utf8NoBom.GetString(ms.ToArray());
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, json, Utf8NoBom);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    File.Move(tmp, path);
                }
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("[settings] save failed: " + ex.Message);
            }
        }

        private static PluginSettings CreateDefault()
        {
            return new PluginSettings();
        }
    }
}
