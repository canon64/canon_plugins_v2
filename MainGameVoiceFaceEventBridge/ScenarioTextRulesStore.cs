using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameVoiceFaceEventBridge
{
    internal static class ScenarioTextRulesStore
    {
        internal const string FileName = "ScenarioTextRules.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static ScenarioTextRules LoadOrCreate(
            string pluginDir,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            string path = GetPath(pluginDir);
            try
            {
                if (!File.Exists(path))
                {
                    ScenarioTextRules created = ScenarioTextRules.CreateDefault();
                    created.Normalize();
                    Save(path, created);
                    logInfo?.Invoke("[scenario-rules] created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                ScenarioTextRules parsed = Deserialize(json);
                if (parsed == null)
                {
                    logWarn?.Invoke("[scenario-rules] parse failed, fallback to default");
                    parsed = ScenarioTextRules.CreateDefault();
                }

                parsed.Normalize();
                Save(path, parsed);
                return parsed;
            }
            catch (Exception ex)
            {
                logError?.Invoke("[scenario-rules] load failed: " + ex.Message);
                ScenarioTextRules fallback = ScenarioTextRules.CreateDefault();
                fallback.Normalize();
                return fallback;
            }
        }

        internal static string GetPath(string pluginDir)
        {
            return Path.Combine(pluginDir, FileName);
        }

        internal static void Save(string path, ScenarioTextRules rules)
        {
            if (rules == null)
            {
                rules = ScenarioTextRules.CreateDefault();
            }

            rules.Normalize();
            var serializer = new DataContractJsonSerializer(typeof(ScenarioTextRules));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, rules);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private static ScenarioTextRules Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(ScenarioTextRules));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as ScenarioTextRules;
            }
        }
    }
}
