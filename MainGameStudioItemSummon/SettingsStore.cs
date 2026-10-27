using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameStudioItemSummon
{
    internal static class SettingsStore
    {
        private const string FileName = "StudioItemSummonSettings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static PluginSettings LoadOrCreate(
            string pluginDir,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            string path = Path.Combine(pluginDir, FileName);
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

                // 新規項目を可視化するためスキーマを書き戻す。
                Save(path, parsed);
                return parsed;
            }
            catch (Exception ex)
            {
                logError?.Invoke($"settings load failed: {ex.Message}");
                return new PluginSettings();
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
