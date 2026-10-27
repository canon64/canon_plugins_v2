using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using CanonPlugins.Shared;
using UnityEngine;

namespace MainGameCharacterAfterimage
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.characterafterimage";
        public const string PluginName = "MainGameCharacterAfterimage";
        public const string Version = "0.1.0";

        private SimpleFileLogger _fileLogger;
        private LayeredAfterimagePipeline _pipeline;
        private Camera _sourceCamera;
        private float _nextCameraResolveTime;
        private float _nextStatusLogTime;
        private bool _runtimeEnabledLast;
        private Action _onBlankRoomLeft;

        private void Awake()
        {
            _onBlankRoomLeft = () => ShutdownPipeline("blank room left");
            _fileLogger = new SimpleFileLogger();

            string pluginDir = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(pluginDir))
            {
                pluginDir = Paths.PluginPath;
            }
            Directory.CreateDirectory(pluginDir);

            string logPath = Path.Combine(pluginDir, "MainGameCharacterAfterimage.log");
            _fileLogger.Initialize(logPath);

            _settingsPath = Path.Combine(pluginDir, SettingsFileName);
            LoadSettings();
            SetupConfigBindings();

            _pipeline = new LayeredAfterimagePipeline(LogInfo, LogWarn, LogDebug);
            LogInfo(PluginName + " " + Version + " loaded");
            LogInfo("settings path: " + _settingsPath);
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void Update()
        {
            // 部屋を出たら、掛かっていた残像をパイプラインごと畳む。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, _onBlankRoomLeft)) return;

            if (Bindings.Down("MainGameCharacterAfterimage.reload", "R + LeftControl"))
            {
                LoadSettings();
                SaveSettings(createBackup: true);
                ApplyConfigToSettings("ctrl+r");
                if (_pipeline != null)
                {
                    _pipeline.UpdateSettings(_settings);
                }
                LogInfo("settings reloaded by Ctrl+R");
            }

            bool runtimeEnabled = _settings != null && _settings.Enabled;
            if (runtimeEnabled != _runtimeEnabledLast)
            {
                _runtimeEnabledLast = runtimeEnabled;
                LogDebug("runtime enabled: " + runtimeEnabled);
            }

            if (!runtimeEnabled)
            {
                ShutdownPipeline("disabled by settings");
                return;
            }

            if (Time.unscaledTime >= _nextCameraResolveTime)
            {
                _nextCameraResolveTime = Time.unscaledTime + 0.5f;
                Camera resolved = ResolveSourceCamera();
                if (resolved != _sourceCamera)
                {
                    _sourceCamera = resolved;
                    if (_sourceCamera != null)
                    {
                        RebindPipeline(_sourceCamera);
                    }
                    else
                    {
                        ShutdownPipeline("source camera not found");
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (!BlankRoomGate.IsInBlankRoom)
            {
                return;
            }

            if (_settings == null || !_settings.Enabled)
            {
                return;
            }

            if (_sourceCamera == null || !_sourceCamera)
            {
                return;
            }

            if (_pipeline == null)
            {
                _pipeline = new LayeredAfterimagePipeline(LogInfo, LogWarn, LogDebug);
                _pipeline.Bind(_sourceCamera, _settings);
            }
            else if (!_pipeline.IsBoundTo(_sourceCamera))
            {
                RebindPipeline(_sourceCamera);
            }
            _pipeline.Tick();
            WritePeriodicStatus();
        }

        private void OnDestroy()
        {
            ShutdownPipeline("plugin destroy");
            _fileLogger = null;
            _sourceCamera = null;
        }

        private void RebindPipeline(Camera camera)
        {
            ShutdownPipeline("rebind");
            if (camera == null || !camera)
            {
                return;
            }

            _pipeline = new LayeredAfterimagePipeline(LogInfo, LogWarn, LogDebug);
            _pipeline.Bind(camera, _settings);
        }

        private void ShutdownPipeline(string reason)
        {
            if (_pipeline != null)
            {
                _pipeline.Dispose();
                _pipeline = null;
                LogDebug("pipeline shutdown: " + reason);
            }
        }

        private Camera ResolveSourceCamera()
        {
            if (_settings == null)
            {
                return null;
            }

            string nameContains = _settings.SourceCameraNameContains ?? string.Empty;
            bool hasNameFilter = nameContains.Length > 0;

            if (_settings.PreferCameraMain && Camera.main != null && Camera.main.enabled && !IsPipelineOwnedCamera(Camera.main))
            {
                if (!hasNameFilter || Camera.main.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Camera.main;
                }
            }

            Camera[] all = Camera.allCameras;
            if (all == null || all.Length == 0)
            {
                return null;
            }

            var sorted = new List<Camera>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Camera cam = all[i];
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (IsPipelineOwnedCamera(cam))
                {
                    continue;
                }
                sorted.Add(cam);
            }

            if (sorted.Count == 0)
            {
                return null;
            }

            sorted.Sort((a, b) => b.depth.CompareTo(a.depth));

            if (hasNameFilter)
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return sorted[i];
                    }
                }
            }

            int idx = Mathf.Clamp(_settings.SourceCameraFallbackIndex, 0, sorted.Count - 1);
            return sorted[idx];
        }

        private bool IsPipelineOwnedCamera(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }
            if (_pipeline != null && _pipeline.OwnsCamera(camera))
            {
                return true;
            }
            if (camera.name.StartsWith("Afterimage", StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }

        private void WritePeriodicStatus()
        {
            if (_settings == null || _settings.StatusLogIntervalSec <= 0f)
            {
                return;
            }
            if (Time.unscaledTime < _nextStatusLogTime)
            {
                return;
            }
            _nextStatusLogTime = Time.unscaledTime + _settings.StatusLogIntervalSec;

            if (_pipeline == null)
            {
                LogInfo("status: pipeline=off");
                return;
            }

            _pipeline.GetStats(out int activeSlots, out int totalSlots, out int width, out int height);
            LogInfo(
                "status: cam=" + (_sourceCamera != null ? _sourceCamera.name : "(null)")
                + " activeSlots=" + activeSlots
                + "/" + totalSlots
                + " capture=" + width + "x" + height
                + " fadeFrames=" + _settings.FadeFrames
                + " interval=" + _settings.CaptureIntervalFrames);
        }

        private void LogInfo(string message)
        {
            if (!ShouldEmitInfoOrDebug())
            {
                return;
            }

            Logger.LogInfo(message);
            _fileLogger?.Write("INFO", message);
        }

        private void LogWarn(string message)
        {
            Logger.LogWarning(message);
            _fileLogger?.Write("WARN", message);
        }

        private void LogDebug(string message)
        {
            if (!ShouldEmitInfoOrDebug())
            {
                return;
            }

            if (_settings == null || !_settings.VerboseLog)
            {
                return;
            }

            Logger.LogInfo(message);
            _fileLogger?.Write("DEBUG", message);
        }

        private bool ShouldEmitInfoOrDebug()
        {
            if (_settings == null)
            {
                return true;
            }

            return _settings.Enabled;
        }
    }
}
