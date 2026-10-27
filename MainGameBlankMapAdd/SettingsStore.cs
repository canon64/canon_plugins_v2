using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameBlankMapAdd
{
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static PluginSettings LoadOrCreate(
            string pluginDir,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            string path = Path.Combine(pluginDir, "MapAddSettings.json");
            try
            {
                if (!File.Exists(path))
                {
                    var created = new PluginSettings();
                    Save(path, created);
                    logInfo?.Invoke($"settings created: {path}");
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                var parsed = Deserialize(json);
                if (parsed == null)
                {
                    logWarn?.Invoke("settings parse failed, fallback to default");
                    return new PluginSettings();
                }

                // Keep file schema up to date so newly added options are visible/editable.
                Save(path, parsed);

                return parsed;
            }
            catch (Exception ex)
            {
                logError?.Invoke($"settings load failed: {ex.Message}");
                return new PluginSettings();
            }
        }

        internal static bool TryLoadFromPath(string path, out PluginSettings settings, out string error)
        {
            settings = null;
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "path is empty";
                    return false;
                }

                if (!File.Exists(path))
                {
                    error = "file not found";
                    return false;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                settings = Deserialize(json);
                if (settings == null)
                {
                    error = "deserialize failed";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static void Save(string path, PluginSettings settings)
        {
            var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, settings ?? new PluginSettings());
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private static PluginSettings Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as PluginSettings;
            }
        }
    }
}
