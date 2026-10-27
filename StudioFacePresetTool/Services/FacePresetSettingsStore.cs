using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace StudioFacePresetTool.Services
{
    internal sealed class FacePresetSettingsStore
    {
        internal const string FileName = "StudioFacePresetToolSettings.json";

        private readonly string _path;
        private readonly PluginLogger _log;
        private readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        internal FacePresetSettingsStore(string pluginDir, PluginLogger log)
        {
            _path = Path.Combine(pluginDir, FileName);
            _log = log;
        }

        internal FacePresetToolSettings LoadOrCreate()
        {
            _log.Entry(nameof(LoadOrCreate));
            try
            {
                bool exists = File.Exists(_path);
                _log.Branch("settings-file-exists", exists, _path);
                if (!exists)
                {
                    FacePresetToolSettings created = Normalize(new FacePresetToolSettings());
                    Save(created);
                    _log.Info("settings created path=" + _path);
                    return created;
                }

                string json = File.ReadAllText(_path, Encoding.UTF8);
                bool empty = string.IsNullOrWhiteSpace(json);
                _log.Branch("settings-json-empty", empty, "len=" + (json == null ? 0 : json.Length));
                if (empty)
                {
                    FacePresetToolSettings created = Normalize(new FacePresetToolSettings());
                    Save(created);
                    _log.Warn("settings empty, recreated");
                    return created;
                }

                FacePresetToolSettings loaded = JsonUtility.FromJson<FacePresetToolSettings>(json);
                bool parsed = loaded != null;
                _log.Branch("settings-json-parsed", parsed, string.Empty);
                if (!parsed)
                {
                    loaded = new FacePresetToolSettings();
                }

                loaded = Normalize(loaded);
                return loaded;
            }
            catch (Exception ex)
            {
                _log.Error("settings load failed", ex);
                FacePresetToolSettings fallback = Normalize(new FacePresetToolSettings());
                Save(fallback);
                return fallback;
            }
        }

        internal void Save(FacePresetToolSettings settings)
        {
            _log.Entry(nameof(Save));
            try
            {
                FacePresetToolSettings normalized = Normalize(settings);
                string json = JsonUtility.ToJson(normalized, true);
                File.WriteAllText(_path, json, _utf8NoBom);
                _log.Info("settings saved path=" + _path);
            }
            catch (Exception ex)
            {
                _log.Error("settings save failed", ex);
            }
        }

        private FacePresetToolSettings Normalize(FacePresetToolSettings settings)
        {
            _log.Entry(nameof(Normalize));
            if (settings == null)
            {
                _log.Branch("settings-null", true, string.Empty);
                settings = new FacePresetToolSettings();
            }
            else
            {
                _log.Branch("settings-null", false, string.Empty);
            }

            if (settings.Window == null)
            {
                _log.Branch("settings-window-null", true, string.Empty);
                settings.Window = new WindowRectData();
            }
            else
            {
                _log.Branch("settings-window-null", false, string.Empty);
            }

            settings.Window.X = Round2(settings.Window.X);
            settings.Window.Y = Round2(settings.Window.Y);
            settings.Window.Width = Mathf.Max(460f, Round2(settings.Window.Width));
            settings.Window.Height = Mathf.Max(420f, Round2(settings.Window.Height));

            if (string.IsNullOrWhiteSpace(settings.ToggleUiKey))
            {
                _log.Branch("toggle-key-empty", true, string.Empty);
                settings.ToggleUiKey = "F9";
            }
            else
            {
                _log.Branch("toggle-key-empty", false, settings.ToggleUiKey);
                settings.ToggleUiKey = settings.ToggleUiKey.Trim();
            }

            settings.SelectedPresetIndex = Mathf.Max(-1, settings.SelectedPresetIndex);
            return settings;
        }

        private static float Round2(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
