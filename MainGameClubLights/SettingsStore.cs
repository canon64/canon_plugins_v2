using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameClubLights
{
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(typeof(ClubLightsSettings));
        }

        internal static ClubLightsSettings Load(string path)
        {
            if (!File.Exists(path))
                return new ClubLightsSettings();

            try
            {
                var serializer = CreateSerializer();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var loaded = serializer.ReadObject(fs) as ClubLightsSettings;
                    if (loaded != null)
                        return loaded;
                }
            }
            catch (Exception ex)
            {
                var logger = Plugin.Instance?._log;
                string msg = $"[Settings] DCJ読み込み失敗 path={path} ex={ex.GetType().Name}:{ex.Message}";
                if (logger != null) logger.Warn(msg);
                else Debug.LogWarning(msg);
                if (ex.InnerException != null)
                {
                    string innerMsg = $"[Settings] DCJ inner={ex.InnerException.GetType().Name}:{ex.InnerException.Message}";
                    if (logger != null) logger.Warn(innerMsg);
                    else Debug.LogWarning(innerMsg);
                }
            }

            // 旧形式フォールバック
            try
            {
                string json = File.ReadAllText(path, Utf8NoBom);
                var legacy = JsonUtility.FromJson<ClubLightsSettings>(json);
                var logger = Plugin.Instance?._log;
                logger?.Warn($"[Settings] JsonUtilityフォールバック使用 path={path} lights={legacy?.Lights?.Count ?? -1}");
                return legacy ?? new ClubLightsSettings();
            }
            catch (Exception ex)
            {
                var logger = Plugin.Instance?._log;
                string msg = $"[Settings] JsonUtility失敗 path={path} ex={ex.Message}";
                if (logger != null) logger.Warn(msg);
                else Debug.LogWarning(msg);
                return new ClubLightsSettings();
            }
        }

        internal static bool Save(string path, ClubLightsSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var serializer = CreateSerializer();
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(fs, settings ?? new ClubLightsSettings());
                    fs.Flush(true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClubLights] 設定保存失敗: {ex.Message}");
                return false;
            }
        }
    }
}
