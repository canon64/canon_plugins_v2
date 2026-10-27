using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameFreeHMasturbationMenu
{
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static PluginSettings LoadOrCreate(
            string pluginDir,
            string settingsFileName,
            Action<string> info,
            Action<string> warn,
            Action<string> error)
        {
            string path = Path.Combine(pluginDir, settingsFileName);
            try
            {
                if (!File.Exists(path))
                {
                    PluginSettings created = new PluginSettings();
                    Save(path, created);
                    info?.Invoke("settings created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                PluginSettings parsed = Deserialize(json);
                if (parsed == null)
                {
                    warn?.Invoke("settings parse failed; fallback defaults");
                    return new PluginSettings();
                }

                Save(path, parsed);
                return parsed;
            }
            catch (Exception ex)
            {
                error?.Invoke("settings load failed: " + ex.Message);
                return new PluginSettings();
            }
        }

        internal static void Save(string path, PluginSettings settings)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, settings ?? new PluginSettings());
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private static PluginSettings Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as PluginSettings;
            }
        }
    }
}
