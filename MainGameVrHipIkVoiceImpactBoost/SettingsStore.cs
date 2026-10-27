using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal static class SettingsStore
    {
        internal const string FileName = "MainGameVrHipIkVoiceImpactBoostSettings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static string GetPath(string pluginDir)
        {
            return Path.Combine(pluginDir, FileName);
        }

        internal static SettingsJsonDto LoadOrNull(
            string pluginDir,
            Action<string> logWarn,
            Action<string> logError)
        {
            string path = GetPath(pluginDir);
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var serializer = new DataContractJsonSerializer(typeof(SettingsJsonDto));
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using (var ms = new MemoryStream(bytes))
                {
                    return serializer.ReadObject(ms) as SettingsJsonDto;
                }
            }
            catch (Exception ex)
            {
                logError?.Invoke("settings load failed: " + ex.Message);
                return null;
            }
        }

        internal static void Save(
            string pluginDir,
            SettingsJsonDto dto,
            Action<string> logError)
        {
            string path = GetPath(pluginDir);
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SettingsJsonDto));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, dto ?? new SettingsJsonDto());
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(path, json, Utf8NoBom);
                }
            }
            catch (Exception ex)
            {
                logError?.Invoke("settings save failed: " + ex.Message);
            }
        }
    }
}
