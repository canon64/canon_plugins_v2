using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private void LoadSettingsOrCreate()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    _settings = NormalizeSettings(new ComposerSettings());
                    SaveSettings(createBackup: false);
                    LogInfo("settings created");
                    return;
                }

                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                ComposerSettings parsed = JsonUtility.FromJson<ComposerSettings>(json);
                if (parsed == null)
                {
                    LogWarn("settings parse failed; defaults applied");
                    parsed = new ComposerSettings();
                }

                _settings = NormalizeSettings(parsed);
                SaveSettings(createBackup: true);
            }
            catch (Exception ex)
            {
                _settings = NormalizeSettings(new ComposerSettings());
                LogError("settings load failed: " + ex.Message);
            }
        }

        private void SaveSettings(bool createBackup = true)
        {
            try
            {
                _settings = NormalizeSettings(_settings);
                string json = JsonUtility.ToJson(_settings, true);
                SaveJsonAtomic(_settingsPath, json, createBackup);
            }
            catch (Exception ex)
            {
                LogError("settings save failed: " + ex.Message);
            }
        }

        private ComposerSettings NormalizeSettings(ComposerSettings settings)
        {
            if (settings == null)
            {
                settings = new ComposerSettings();
            }

            if (!Enum.IsDefined(typeof(KeyCode), settings.ToggleUiKey))
            {
                settings.ToggleUiKey = KeyCode.F8;
            }
            if (!Enum.IsDefined(typeof(KeyCode), settings.ToggleStateKey))
            {
                settings.ToggleStateKey = KeyCode.F7;
            }

            settings.MainWindowW = Mathf.Clamp(settings.MainWindowW, 360f, 1800f);
            settings.MainWindowH = Mathf.Clamp(settings.MainWindowH, 320f, 1400f);
            settings.StateWindowW = Mathf.Clamp(settings.StateWindowW, 360f, 1600f);
            settings.StateWindowH = Mathf.Clamp(settings.StateWindowH, 260f, 1200f);

            settings.MaxUndoSteps = Mathf.Clamp(settings.MaxUndoSteps, 8, 2048);
            settings.DiskFlattenYScale = Mathf.Clamp(settings.DiskFlattenYScale, 0.001f, 10f);

            settings.PositionNudgeStep = Mathf.Clamp(settings.PositionNudgeStep, 0.0001f, 10f);
            settings.RotationNudgeStep = Mathf.Clamp(settings.RotationNudgeStep, 0.01f, 180f);
            settings.ScaleNudgeStep = Mathf.Clamp(settings.ScaleNudgeStep, 0.0001f, 10f);

            if (settings.DefaultScale.x <= 0f) settings.DefaultScale.x = 1f;
            if (settings.DefaultScale.y <= 0f) settings.DefaultScale.y = 1f;
            if (settings.DefaultScale.z <= 0f) settings.DefaultScale.z = 1f;

            if (settings.DefaultChildOffset == Vector3.zero)
            {
                settings.DefaultChildOffset = new Vector3(0.5f, 0f, 0f);
            }

            if (settings.DefaultAutoRotateAxis.sqrMagnitude < 1e-6f)
            {
                settings.DefaultAutoRotateAxis = Vector3.up;
            }

            settings.DefaultAutoRotateSpeedDegPerSec = Mathf.Clamp(settings.DefaultAutoRotateSpeedDegPerSec, -2000f, 2000f);

            if (settings.DefaultAngleAxis.sqrMagnitude < 1e-6f)
            {
                settings.DefaultAngleAxis = Vector3.up;
            }
            settings.DefaultAngleAmplitudeDeg = Mathf.Clamp(settings.DefaultAngleAmplitudeDeg, 0f, 180f);
            settings.DefaultAngleSpeedHz = Mathf.Clamp(settings.DefaultAngleSpeedHz, 0.01f, 20f);

            if (settings.DefaultPistonAxis.sqrMagnitude < 1e-6f)
            {
                settings.DefaultPistonAxis = Vector3.forward;
            }
            settings.DefaultPistonAmplitude = Mathf.Clamp(settings.DefaultPistonAmplitude, 0f, 10f);
            settings.DefaultPistonSpeedHz = Mathf.Clamp(settings.DefaultPistonSpeedHz, 0.01f, 20f);

            settings.DefaultSpawnDistance = Mathf.Clamp(settings.DefaultSpawnDistance, 0.05f, 10f);

            settings.GizmoSizeMultiplier = Mathf.Clamp(settings.GizmoSizeMultiplier, 0.2f, 4f);

            return settings;
        }

        private void LoadLayoutOrCreate(bool rebuildRuntime)
        {
            try
            {
                if (!File.Exists(_layoutPath))
                {
                    ApplyLayoutData(new ObjectLayoutFile(), rebuildRuntime);
                    SaveLayout(createBackup: false);
                    LogInfo("layout created");
                    return;
                }

                string json = File.ReadAllText(_layoutPath, Encoding.UTF8);
                ObjectLayoutFile parsed = JsonUtility.FromJson<ObjectLayoutFile>(json);
                if (parsed == null)
                {
                    parsed = new ObjectLayoutFile();
                    LogWarn("layout parse failed; empty layout loaded");
                }

                ApplyLayoutData(parsed, rebuildRuntime);
            }
            catch (Exception ex)
            {
                ApplyLayoutData(new ObjectLayoutFile(), rebuildRuntime);
                LogError("layout load failed: " + ex.Message);
            }
        }

        private void SaveLayout(bool createBackup = true)
        {
            try
            {
                SyncAllDataFromRuntime();
                var file = new ObjectLayoutFile
                {
                    format = "ObjectLayoutV2",
                    objectsJson = EncodeObjects(_objects),
                    selectedId = _selectedId,
                    parentCandidateKind = _parentCandidateKind,
                    parentCandidateRefId = _parentCandidateRefId,
                    parentCandidateId = string.Equals(_parentCandidateKind, ParentKindManaged, StringComparison.Ordinal)
                        ? _parentCandidateRefId
                        : null
                };

                string json = JsonUtility.ToJson(file, true);
                SaveJsonAtomic(_layoutPath, json, createBackup);
            }
            catch (Exception ex)
            {
                LogError("layout save failed: " + ex.Message);
            }
        }

        private void SaveJsonAtomic(string path, string json, bool createBackup)
        {
            string tempPath = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(tempPath, (json ?? "{}") + Environment.NewLine, Utf8NoBom);

                // .bak ファイルは作らない方針（SaveJsonAtomic 自体で書き込み中の壊れは防げる）

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

    }
}
