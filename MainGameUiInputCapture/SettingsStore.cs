using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameUiInputCapture
{
    internal static class SettingsStore
    {
        internal const string FileName = "MainGameUiInputCaptureSettings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static UiInputCaptureSettings LoadOrCreate(
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
                    var created = Normalize(new UiInputCaptureSettings());
                    SaveInternal(path, created, false, logWarn);
                    logInfo?.Invoke("settings created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                UiInputCaptureSettings parsed = JsonUtility.FromJson<UiInputCaptureSettings>(json);
                if (parsed == null)
                {
                    logWarn?.Invoke("settings parse failed, using defaults");
                    parsed = new UiInputCaptureSettings();
                }

                parsed = Normalize(parsed);
                SaveInternal(path, parsed, false, logWarn);
                return parsed;
            }
            catch (Exception ex)
            {
                logError?.Invoke("settings load failed: " + ex.Message);
                return Normalize(new UiInputCaptureSettings());
            }
        }

        internal static void Save(string pluginDir, UiInputCaptureSettings settings, Action<string> logWarn)
        {
            string path = Path.Combine(pluginDir, FileName);
            SaveInternal(path, Normalize(settings), false, logWarn);
        }

        private static UiInputCaptureSettings Normalize(UiInputCaptureSettings settings)
        {
            if (settings == null)
                settings = new UiInputCaptureSettings();

            return settings;
        }

        private static void SaveInternal(string path, UiInputCaptureSettings settings, bool backup, Action<string> logWarn)
        {
            string tempPath = path + ".tmp";
            try
            {
                string json = JsonUtility.ToJson(settings ?? new UiInputCaptureSettings(), true);
                File.WriteAllText(tempPath, json + Environment.NewLine, Utf8NoBom);

                if (backup && File.Exists(path))
                    CreateBackup(path, logWarn);

                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void CreateBackup(string path, Action<string> logWarn)
        {
            try
            {
                string backupPath = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(path, backupPath, true);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("settings backup failed: " + ex.Message);
            }
        }
    }
}
