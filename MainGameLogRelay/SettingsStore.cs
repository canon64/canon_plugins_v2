using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameLogRelay
{
    internal static class SettingsStore
    {
        internal const string FileName = "MainGameLogRelaySettings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static MainGameLogRelaySettings LoadOrCreate(
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
                    var created = Normalize(new MainGameLogRelaySettings());
                    SaveInternal(path, created, false, logWarn);
                    logInfo?.Invoke("settings created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                MainGameLogRelaySettings parsed = JsonUtility.FromJson<MainGameLogRelaySettings>(json);
                if (parsed == null)
                {
                    logWarn?.Invoke("settings parse failed, using defaults");
                    parsed = new MainGameLogRelaySettings();
                }

                parsed = Normalize(parsed);
                SaveInternal(path, parsed, false, logWarn);
                return parsed;
            }
            catch (Exception ex)
            {
                logError?.Invoke("settings load failed: " + ex.Message);
                return Normalize(new MainGameLogRelaySettings());
            }
        }

        internal static void Save(string pluginDir, MainGameLogRelaySettings settings, Action<string> logWarn)
        {
            string path = Path.Combine(pluginDir, FileName);
            SaveInternal(path, Normalize(settings), false, logWarn);
        }

        private static MainGameLogRelaySettings Normalize(MainGameLogRelaySettings settings)
        {
            if (settings == null)
                settings = new MainGameLogRelaySettings();

            if (settings.OwnerRules == null)
                settings.OwnerRules = new OwnerRule[0];

            settings.DefaultMinimumLevel = NormalizeLevel(settings.DefaultMinimumLevel);
            settings.DefaultOutputMode = NormalizeMode(settings.DefaultOutputMode);
            settings.FileLayout = NormalizeFileLayout(settings.FileLayout);

            for (int i = 0; i < settings.OwnerRules.Length; i++)
            {
                OwnerRule rule = settings.OwnerRules[i];
                if (rule == null)
                {
                    settings.OwnerRules[i] = new OwnerRule();
                    continue;
                }

                rule.Owner = rule.Owner == null ? string.Empty : rule.Owner.Trim();
                rule.LogKey = rule.LogKey ?? string.Empty;
                rule.MinimumLevel = NormalizeLevel(rule.MinimumLevel);
                rule.OutputMode = NormalizeMode(rule.OutputMode);
                rule.FileLayout = NormalizeFileLayout(rule.FileLayout);
            }

            return settings;
        }

        private static LogRelayLevel NormalizeLevel(LogRelayLevel level)
        {
            if (level < LogRelayLevel.Debug)
                return LogRelayLevel.Debug;
            if (level > LogRelayLevel.Error)
                return LogRelayLevel.Error;
            return level;
        }

        private static LogRelayOutputMode NormalizeMode(LogRelayOutputMode mode)
        {
            if (mode < LogRelayOutputMode.FileOnly)
                return LogRelayOutputMode.FileOnly;
            if (mode > LogRelayOutputMode.Both)
                return LogRelayOutputMode.Both;
            return mode;
        }

        private static LogRelayFileLayout NormalizeFileLayout(LogRelayFileLayout layout)
        {
            if (layout < LogRelayFileLayout.PerPlugin)
                return LogRelayFileLayout.PerPlugin;
            if (layout > LogRelayFileLayout.Shared)
                return LogRelayFileLayout.Shared;
            return layout;
        }

        private static void SaveInternal(string path, MainGameLogRelaySettings settings, bool backup, Action<string> logWarn)
        {
            string tempPath = path + ".tmp";
            try
            {
                string json = JsonUtility.ToJson(settings ?? new MainGameLogRelaySettings(), true);
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
