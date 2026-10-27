using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private const string DefaultProfileName = "default";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        [Serializable]
        private sealed class ProfileState
        {
            public string LastProfileName = DefaultProfileName;
        }

        internal string CurrentProfileName
        {
            get { return string.IsNullOrEmpty(_currentProfileName) ? DefaultProfileName : _currentProfileName; }
        }

        private void InitializeProfileStorage()
        {
            _legacySettingsPath = Path.Combine(_pluginDir, "ClubLightsSettings.json");
            _profilesDir = Path.Combine(_pluginDir, "profiles");
            _profileStatePath = Path.Combine(_profilesDir, "state.json");
            Directory.CreateDirectory(_profilesDir);

            string requestedName = LoadLastProfileName();
            if (TryLoadStartupProfile(requestedName))
            {
                SyncProfileUiBuffersFromCurrent();
                return;
            }

            if (File.Exists(_legacySettingsPath))
            {
                _settings = SettingsStore.Load(_legacySettingsPath);
                EnsureSettingsValid();

                _currentProfileName = DefaultProfileName;
                _settingsPath = GetProfilePath(_currentProfileName);
                if (!SettingsStore.Save(_settingsPath, _settings))
                    _log.Warn($"[Profile] legacy移行保存失敗 path={_settingsPath}");

                SaveLastProfileName(_currentProfileName);
                _log.Info($"[Profile] legacy設定を移行 name={_currentProfileName} path={_settingsPath}");
                SyncProfileUiBuffersFromCurrent();
                return;
            }

            _currentProfileName = DefaultProfileName;
            _settingsPath = GetProfilePath(_currentProfileName);
            _settings = SettingsStore.Load(_settingsPath);
            EnsureSettingsValid();

            if (!File.Exists(_settingsPath))
                SettingsStore.Save(_settingsPath, _settings);

            SaveLastProfileName(_currentProfileName);
            _log.Info($"[Profile] 初期化 name={_currentProfileName} path={_settingsPath}");
            SyncProfileUiBuffersFromCurrent();
        }

        internal List<string> ListProfileNames()
        {
            var names = new List<string>();
            try
            {
                if (!Directory.Exists(_profilesDir))
                    return names;

                string[] files = Directory.GetFiles(_profilesDir, "*.json");
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, "state", StringComparison.OrdinalIgnoreCase)) continue;
                    names.Add(name);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _log.Warn($"[Profile] 一覧取得失敗: {ex.Message}");
            }

            return names;
        }

        internal bool SaveCurrentProfile(out string message)
        {
            string name = CurrentProfileName;
            string path = GetProfilePath(name);
            if (!SaveSettingsToPath(path, $"profile-overwrite:{name}"))
            {
                message = $"上書き保存失敗: {name}";
                return false;
            }

            _currentProfileName = name;
            _settingsPath = path;
            SaveLastProfileName(name);
            message = $"上書き保存: {name}";
            return true;
        }

        internal bool SaveProfileAsNew(string rawName, out string message)
        {
            string name = NormalizeProfileName(rawName);
            if (string.IsNullOrEmpty(name))
            {
                message = "保存失敗: プロファイル名を入力してください";
                return false;
            }

            string path = GetProfilePath(name);
            if (File.Exists(path))
            {
                message = $"保存失敗: 同名が存在 ({name})";
                return false;
            }

            if (!SaveSettingsToPath(path, $"profile-save-as:{name}"))
            {
                message = $"保存失敗: {name}";
                return false;
            }

            _currentProfileName = name;
            _settingsPath = path;
            SaveLastProfileName(name);
            message = $"保存完了: {name}";
            return true;
        }

        internal bool LoadProfileByName(string rawName, out string message)
        {
            string name = NormalizeProfileName(rawName);
            if (string.IsNullOrEmpty(name))
            {
                message = "呼び出し失敗: プロファイル名を指定してください";
                return false;
            }

            string path = GetProfilePath(name);
            if (!File.Exists(path))
            {
                message = $"呼び出し失敗: 見つからない ({name})";
                return false;
            }

            _currentProfileName = name;
            _settingsPath = path;
            if (!ReloadSettingsFromDisk($"profile-load:{name}"))
            {
                message = $"呼び出し失敗: {name}";
                return false;
            }

            SaveLastProfileName(name);
            message = $"呼び出し完了: {name}";
            return true;
        }

        internal bool DeleteProfileByName(string rawName, out string message)
        {
            string name = NormalizeProfileName(rawName);
            if (string.IsNullOrEmpty(name))
            {
                message = "削除失敗: プロファイル名を指定してください";
                return false;
            }
            if (string.Equals(name, CurrentProfileName, StringComparison.OrdinalIgnoreCase))
            {
                message = $"削除失敗: 使用中プロファイル ({name})";
                return false;
            }

            string path = GetProfilePath(name);
            if (!File.Exists(path))
            {
                message = $"削除失敗: 見つからない ({name})";
                return false;
            }

            try
            {
                File.Delete(path);
                _log.Info($"[Profile] 削除完了 name={name} path={path}");
                message = $"削除完了: {name}";
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"[Profile] 削除失敗 name={name} ex={ex.Message}");
                message = $"削除失敗: {ex.Message}";
                return false;
            }
        }

        private bool TryLoadStartupProfile(string rawName)
        {
            string name = NormalizeProfileName(rawName);
            if (string.IsNullOrEmpty(name))
                return false;

            string path = GetProfilePath(name);
            if (!File.Exists(path))
                return false;

            _currentProfileName = name;
            _settingsPath = path;
            _settings = SettingsStore.Load(path);
            EnsureSettingsValid();
            SaveLastProfileName(name);
            _log.Info($"[Profile] 起動時読込 name={name} path={path}");
            return true;
        }

        private bool SaveSettingsToPath(string path, string reason)
        {
            try
            {
                if (_insideHScene)
                    SyncFreeGizmoPositions();

                EnsureSettingsValid();
                int lightsCount = _settings?.Lights?.Count ?? 0;
                int presetsCount = _settings?.Presets?.Count ?? 0;
                int mapsCount = _settings?.VideoPresetMappings?.Count ?? 0;
                bool ok = SettingsStore.Save(path, _settings);
                if (!ok)
                {
                    _log.Error($"[Settings] 保存失敗 reason={reason} path={path}");
                    return false;
                }

                _log.Info($"[Settings] 保存完了 reason={reason} path={path} lights={lightsCount} presets={presetsCount} maps={mapsCount}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"[Settings] 保存失敗 reason={reason} path={path} ex={ex.Message}");
                return false;
            }
        }

        private string GetProfilePath(string rawName)
        {
            string name = NormalizeProfileName(rawName);
            if (string.IsNullOrEmpty(name))
                name = DefaultProfileName;
            return Path.Combine(_profilesDir, name + ".json");
        }

        private static string NormalizeProfileName(string rawName)
        {
            string name = (rawName ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                return "";

            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            name = name.Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(name))
                return "";
            if (string.Equals(name, "state", StringComparison.OrdinalIgnoreCase))
                name = "state_profile";
            if (name.Length > 64)
                name = name.Substring(0, 64);
            return name;
        }

        private string LoadLastProfileName()
        {
            try
            {
                if (!File.Exists(_profileStatePath))
                    return DefaultProfileName;

                string json = File.ReadAllText(_profileStatePath, Utf8NoBom);
                var state = JsonUtility.FromJson<ProfileState>(json);
                string name = NormalizeProfileName(state?.LastProfileName);
                return string.IsNullOrEmpty(name) ? DefaultProfileName : name;
            }
            catch (Exception ex)
            {
                _log.Warn($"[Profile] state読込失敗: {ex.Message}");
                return DefaultProfileName;
            }
        }

        private void SaveLastProfileName(string rawName)
        {
            string name = NormalizeProfileName(rawName);
            if (string.IsNullOrEmpty(name))
                name = DefaultProfileName;

            try
            {
                Directory.CreateDirectory(_profilesDir);
                var state = new ProfileState { LastProfileName = name };
                string json = JsonUtility.ToJson(state, true);
                File.WriteAllText(_profileStatePath, json, Utf8NoBom);
            }
            catch (Exception ex)
            {
                _log.Warn($"[Profile] state保存失敗: {ex.Message}");
            }
        }
    }
}
