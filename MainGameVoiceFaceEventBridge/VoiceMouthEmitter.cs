using System;
using MainGameVoiceFaceEventBridge.Audio;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    // Keep the persistent audio host independent of the character's lifetime.
    [DefaultExecutionOrder(10000)]
    internal sealed class VoiceMouthEmitter : MonoBehaviour
    {
        private AudioSource _source;
        private SpatialVoiceSettings _settings;
        private ChaControl _female;
        private GameObject _root;
        private Transform _mouth;
        private float _nextResolve;
        private Action<string> _log;
        private static float _nextSpatializerCheck;
        private static bool _hasSpatializer;

        internal void Initialize(AudioSource source, SpatialVoiceSettings settings, Action<string> log)
        {
            _source = source;
            _settings = settings;
            _log = log;
            UpdatePosition();
        }

        internal void Bind(ChaControl female)
        {
            if (_female != female)
            {
                _female = female;
                _root = null;
                _mouth = null;
                _nextResolve = 0;
            }
            UpdatePosition(); // Also position before the first audio buffer.
        }

        internal void Clear()
        {
            _female = null;
            _root = null;
            _mouth = null;
            _nextResolve = 0;
            if (_source != null) { _source.spatialBlend = 0; _source.spatialize = false; }
        }

        private void LateUpdate()
        {
            if (_source != null && _source.isPlaying) UpdatePosition();
        }

        internal void UpdatePosition()
        {
            if (_source == null || _settings == null) return;
            float minDistance = _settings.MinDistance.Value;
            float maxDistance = Mathf.Max(minDistance, _settings.MaxDistance.Value);
            AudioRolloffMode rolloff = _settings.Rolloff.Value == "Linear"
                ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic;
            if (_source.dopplerLevel != 0f) _source.dopplerLevel = 0f;
            if (_source.spread != 0f) _source.spread = 0f;
            if (_source.minDistance != minDistance) _source.minDistance = minDistance;
            if (_source.maxDistance != maxDistance) _source.maxDistance = maxDistance;
            if (_source.rolloffMode != rolloff) _source.rolloffMode = rolloff;
            GameObject root = _female != null ? _female.objBodyBone : null;
            if (_root != root) { _root = root; _mouth = null; _nextResolve = 0; }
            if (_root != null && _mouth == null && Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + 1f;
                foreach (Transform bone in _root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(bone.name, "cf_J_MouthCavity", StringComparison.OrdinalIgnoreCase)) continue;
                    _mouth = bone;
                    _log?.Invoke("[spatial-voice] mouth bound: " + _source.gameObject.name);
                    break;
                }
            }
            bool positional = _settings.Enabled.Value && _mouth != null;
            if (positional)
            {
                Vector3 mouthPosition = _mouth.position;
                Quaternion mouthRotation = _mouth.rotation;
                if ((transform.position - mouthPosition).sqrMagnitude > 0.00000001f ||
                    Mathf.Abs(Quaternion.Dot(transform.rotation, mouthRotation)) < 0.999999f)
                    transform.SetPositionAndRotation(mouthPosition, mouthRotation);
            }
            float spatialBlend = positional ? 1f : 0f;
            if (_source.spatialBlend != spatialBlend) _source.spatialBlend = spatialBlend;
            if (Time.unscaledTime >= _nextSpatializerCheck)
            {
                _nextSpatializerCheck = Time.unscaledTime + 1f;
                _hasSpatializer = !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());
            }
            bool spatialize = positional && _hasSpatializer;
            if (_source.spatialize != spatialize) _source.spatialize = spatialize;
        }
    }
}
