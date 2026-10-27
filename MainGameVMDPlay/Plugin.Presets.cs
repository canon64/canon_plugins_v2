using System;
using System.IO;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private string _activePresetPath;
        private bool _presetPlaybackRequested;
        private string _presetName = "";
        private string _presetMessage = "";
        private string[] _presetFiles = new string[0];
        private Vector2 _presetScroll;
        private string PresetDirectory => Path.Combine(_pluginDir, "presets");

        private void RefreshPresets()
        {
            try { Directory.CreateDirectory(PresetDirectory); _presetFiles = Directory.GetFiles(PresetDirectory, "*.json"); Array.Sort(_presetFiles, StringComparer.OrdinalIgnoreCase); InvalidateBarCatalog(); }
            catch (Exception ex) { _presetMessage = ex.Message; }
        }
        private void DrawPresetSection()
        {
            GUILayout.Label("プリセット（VMD・音源・カメラ・調節・変形補正）");
            GUILayout.BeginHorizontal();
            _presetName = GUILayout.TextField(_presetName, 80);
            if (GUILayout.Button("保存 / 上書き", GUILayout.Width(110))) {
                string name = _presetName.Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..")
                    _presetMessage = "有効なプリセット名を入力してください";
                else if (!File.Exists(_settings.LastVmdAbsPath)) _presetMessage = "先にVMDを選択してください";
                else {
                    CaptureAdjustmentValues();
                    string path = Path.Combine(PresetDirectory, name + ".json");
                    bool saved = SettingsStore.Save(path, _settings.PresetCopy());
                    if (saved) _activePresetPath = path;
                    _presetMessage = saved ? "保存しました: " + name : "保存に失敗しました";
                    RefreshPresets();
                }
            }
            if (GUILayout.Button("更新", GUILayout.Width(50))) RefreshPresets();
            GUILayout.EndHorizontal();
            _presetScroll = GUILayout.BeginScrollView(_presetScroll, GUILayout.Height(Mathf.Min(130, 30 * _presetFiles.Length + 8)));
            foreach (string path in _presetFiles) {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(path))) ApplyPreset(path);
                if (GUILayout.Button("＋リスト", GUILayout.Width(85))) AddPresetToMyList(path);
                if (GUILayout.Button("削除", GUILayout.Width(50))) DeletePreset(path);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_presetMessage)) GUILayout.Label(_presetMessage);
        }
        private void DeletePreset(string path)
        {
            try {
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(PresetDirectory).TrimEnd(Path.DirectorySeparatorChar);
                if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return;
                string name = Path.GetFileNameWithoutExtension(full);
                string backupDir = Path.Combine(root, "deleted");
                Directory.CreateDirectory(backupDir);
                File.Move(full, Path.Combine(backupDir, name + "." + System.Guid.NewGuid().ToString("N") + ".json"));
                bool affectsQueue = _myListQueue != null && Array.Exists(_myListQueue, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
                if (affectsQueue || string.Equals(_activePresetPath, full, StringComparison.OrdinalIgnoreCase)) ReleaseCharacter();
                if (string.Equals(_activePresetPath, full, StringComparison.OrdinalIgnoreCase)) _activePresetPath = null;
                if (_settings.MyLists != null) foreach (var list in _settings.MyLists.Values)
                    list?.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
                if (_presetName == name) _presetName = "";
                SaveSettings("preset-delete");
                RefreshPresets();
                _presetMessage = "削除しました: " + name + "（元のVMD・音源は保持）";
            } catch (Exception ex) { _presetMessage = "削除失敗: " + ex.Message; }
        }

        private bool ApplyPreset(string path)
        {
            if (AstralBlocked) return false;
            if (IsIndependentTarget && !_actorReady && _actorRoutine != null) { _queuedActorPreset = path; _presetMessage = "キャラ準備後に再生します"; return true; }
            if (!_myListSwitching) { StopMyList(); _barGroup = ""; }
            if (!_available || !_cfgEnabled.Value || _trialChara == null || !_insideHScene || HasPendingPosture()) { _presetMessage = "キャラ準備完了後・体位変更完了後に選択してください"; return false; }
            var preset = SettingsStore.Load(path);
            if ((!string.IsNullOrEmpty(preset.CameraVmdPath) && !File.Exists(preset.CameraVmdPath)) || !File.Exists(preset.LastVmdAbsPath) || (!string.IsNullOrEmpty(preset.LastAudioAbsPath) && !File.Exists(preset.LastAudioAbsPath))) {
                _presetMessage = "プリセットのVMD・音源・カメラのいずれかが見つかりません"; return false;
            }
            ReleaseCharacter(); VmdBridgeSoundClearClip();
            _musicError = ""; _cameraError = "";
            _settings.LastAudioAbsPath = preset.LastAudioAbsPath;
            _settings.AudioAutoLoad = !string.IsNullOrEmpty(preset.LastAudioAbsPath);
            _settings.AudioLoop = preset.AudioLoop;
            _settings.AudioVolume = NormalizeAudioVolume(preset.AudioVolume);
            _audioVolumeText = null;
            _settings.VideoOnWalls = preset.VideoOnWalls;
            _settings.UseIKDataInVMD = preset.UseIKDataInVMD;
            _settings.SmoothArmTwist = preset.SmoothArmTwist;
            _settings.UseBoneTransform = preset.UseBoneTransform;
            var nextAdjustments = preset.Adjustments ?? new System.Collections.Generic.Dictionary<string, float>();
            for (int i = 0; i < BodyLabels.Length; i++) nextAdjustments.Remove("body." + i);
            _settings.Adjustments = nextAdjustments;
            _settings.CameraVmdPath = preset.CameraVmdPath;
            _adjustmentText.Clear();
            SaveSettings("preset");
            _presetPlaybackRequested = true;
            _presetMessage = "準備中…";
            PlaybackLoadAndPlay(preset.LastVmdAbsPath, true);
            _activePresetPath = path;
            _presetName = Path.GetFileNameWithoutExtension(path);
            if (_vmdOwnsCharacter) _presetMessage = "準備中: " + _presetName;
            return _vmdOwnsCharacter;
        }
    }
}
