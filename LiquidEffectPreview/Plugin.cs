using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiquidEffectPreview
{
    [BepInPlugin(Guid, "LiquidEffectPreview", "1.0.0")]
    [DefaultExecutionOrder(12200)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.canon.liquideffectpreview";
        private PreviewSettings _settings;
        private readonly LiquidPlayback _playback = new LiquidPlayback(41729);
        private LiquidRenderer _renderer;
        private float _retryAt;
        private bool _visible;

        private void Awake()
        {
            _settings = new PreviewSettings(Config);
            SceneManager.activeSceneChanged += SceneChanged;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }
        private void SceneChanged(Scene previous, Scene current) => Clear();
        private void SceneUnloaded(Scene scene) => Clear();
        private void Clear()
        {
            _playback.Clear(); _renderer?.Clear(); _visible = false;
        }
        private void LateUpdate()
        {
            if (_settings == null) return;
            bool request = _settings.EmitOnce.Value;
            if (request) _settings.EmitOnce.Value = false;
            bool held = request || Bindings.Held("LiquidEffectPreview.fire", _settings.FireKey.Value.ToString());
            if (!_settings.Enabled.Value)
            {
                if (_visible) Clear();
                _playback.Tick(0, default, 0, false, false, held, default);
                return;
            }
            Camera camera = Camera.main;
            if (camera == null) { Clear(); return; }
            if (Time.unscaledTime < _retryAt) return;
            try
            {
                if (_renderer == null) _renderer = new LiquidRenderer("PreviewNozzle");
                Transform t = camera.transform;
                Vector3 horizontal = Vector3.ProjectOnPlane(t.forward, Vector3.up);
                if (horizontal.sqrMagnitude < .000001f) horizontal = Vector3.ProjectOnPlane(t.up, Vector3.up);
                if (horizontal.sqrMagnitude < .000001f) horizontal = Vector3.forward;
                horizontal = Quaternion.AngleAxis(_settings.Yaw.Value, Vector3.up) * horizontal.normalized;
                float pitch = _settings.Pitch.Value * Mathf.Deg2Rad;
                Vector3 direction = horizontal * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
                Vector3 position = t.TransformPoint(new Vector3(_settings.NozzleRight.Value, _settings.NozzleUp.Value, _settings.NozzleForward.Value));
                var pose = new EmitterPose(LiquidRenderer.V(position), LiquidRenderer.V(direction), LiquidRenderer.V(Vector3.Cross(Vector3.up, horizontal)));
                bool paused = _settings.Paused.Value || Time.timeScale <= 0;
                _playback.Tick(Time.deltaTime, pose, camera.GetInstanceID(), true, paused, held, _settings.Snapshot());
                _renderer.Render(_playback.Simulation, _settings);
                _visible = true;
            }
            catch (Exception e)
            {
                Clear();
                _renderer?.Dispose(); _renderer = null;
                _retryAt = Time.unscaledTime + 5;
                Logger.LogError("Liquid preview: " + e);
            }
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            _renderer?.Dispose(); _renderer = null;
        }
    }
}
