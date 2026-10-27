using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MainGameBlankMapAdd.Media.WebCam
{
    internal sealed class WebCamSessionService : IDisposable
    {
        private sealed class Binding
        {
            internal WebCamTexture Texture;
            internal readonly List<Material> Materials = new List<Material>();
            internal string DeviceName;
            internal int RequestedWidth;
            internal int RequestedHeight;
            internal int RequestedFps;
            internal int ProfileIndex;
            internal float CreatedAt;
            internal float NextStatusLogTime;
            internal float NextBlackSampleTime;
            internal int RetryCount;
            internal bool LoggedFirstFrame;
        }

        private struct StartProfile
        {
            internal readonly int Width;
            internal readonly int Height;
            internal readonly int Fps;
            internal readonly string Label;

            internal StartProfile(int width, int height, int fps, string label)
            {
                Width = width;
                Height = height;
                Fps = fps;
                Label = label;
            }
        }

        private readonly Func<PluginSettings> _settingsProvider;
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private readonly Dictionary<string, Binding> _bindings =
            new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _updateKeys = new List<string>();
        private float _nextRetryTime;

        internal WebCamSessionService(
            Func<PluginSettings> settingsProvider,
            Action<string> info,
            Action<string> warning)
        {
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        }

        private PluginSettings Settings => _settingsProvider();

        internal void WriteAvailableDevices(string path)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string report = WebCamDeviceCatalog.FormatReport(devices?.Select(device => device.name));
            File.WriteAllText(path, report, Encoding.UTF8);
            _info($"[webcam] デバイス一覧を出力: {path}");
        }

        internal void Attach(Material material, string deviceName)
        {
            if (material == null || string.IsNullOrWhiteSpace(deviceName)) return;
            if (!_bindings.TryGetValue(deviceName, out Binding binding) || binding?.Texture == null)
            {
                binding = CreateBinding(deviceName, 0, null);
                _bindings[deviceName] = binding;
            }
            if (!binding.Materials.Contains(material)) binding.Materials.Add(material);
            BindTexture(binding, material);
            _info($"[webcam] material bound device=\"{deviceName}\"");
        }

        internal void Update()
        {
            if (_bindings.Count == 0 || Time.unscaledTime < _nextRetryTime) return;
            _nextRetryTime = Time.unscaledTime + 1f;
            _updateKeys.Clear();
            foreach (KeyValuePair<string, Binding> pair in _bindings) _updateKeys.Add(pair.Key);

            for (int i = 0; i < _updateKeys.Count; i++)
            {
                string deviceName = _updateKeys[i];
                if (!_bindings.TryGetValue(deviceName, out Binding binding)) continue;
                WebCamTexture texture = binding?.Texture;
                if (texture == null) continue;
                LogStatus(binding, texture);
                if (texture.isPlaying) continue;

                binding.RetryCount++;
                texture.Play();
                _warning(
                    $"[webcam] retry Play device=\"{deviceName}\" retry={binding.RetryCount} listed={HasDevice(deviceName)} " +
                    $"isPlaying={texture.isPlaying} width={texture.width} height={texture.height}");
                if (ShouldRecreate(binding, texture)) Recreate(binding, "not-playing");
            }
        }

        private Binding CreateBinding(string deviceName, int profileIndex, List<Material> existingMaterials)
        {
            StartProfile[] profiles = BuildStartProfiles();
            int clampedIndex = Mathf.Clamp(profileIndex, 0, Mathf.Max(0, profiles.Length - 1));
            StartProfile profile = profiles[clampedIndex];
            WebCamTexture texture = CreateTexture(deviceName, profile);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Play();
            var binding = new Binding
            {
                Texture = texture,
                DeviceName = deviceName,
                RequestedWidth = profile.Width,
                RequestedHeight = profile.Height,
                RequestedFps = profile.Fps,
                ProfileIndex = clampedIndex,
                CreatedAt = Time.unscaledTime,
                NextStatusLogTime = Time.unscaledTime,
                NextBlackSampleTime = Time.unscaledTime + 1f
            };

            if (existingMaterials != null)
            {
                for (int i = 0; i < existingMaterials.Count; i++)
                {
                    Material material = existingMaterials[i];
                    if (material == null || binding.Materials.Contains(material)) continue;
                    binding.Materials.Add(material);
                    BindTexture(binding, material);
                }
            }

            _info(
                $"[webcam-open] device=\"{deviceName}\" profile={clampedIndex}/{profiles.Length - 1} label={profile.Label} " +
                $"requested={FormatRequest(profile)} listed={HasDevice(deviceName)} " +
                $"isPlaying={texture.isPlaying} width={texture.width} height={texture.height}");
            return binding;
        }

        private StartProfile[] BuildStartProfiles()
        {
            PluginSettings settings = Settings;
            int width = Mathf.Max(16, settings?.WebCamRequestedWidth ?? 1920);
            int height = Mathf.Max(16, settings?.WebCamRequestedHeight ?? 1080);
            int fps = Mathf.Clamp(settings?.WebCamRequestedFps ?? 30, 1, 60);
            var profiles = new List<StartProfile>
            {
                new StartProfile(width, height, fps, "settings"),
                new StartProfile(width, height, 60, "settings-60fps"),
                new StartProfile(1920, 1080, 60, "obs-1080p-60fps"),
                new StartProfile(1280, 720, fps, "720p"),
                new StartProfile(1280, 720, 60, "720p-60fps"),
                new StartProfile(640, 480, fps, "vga"),
                new StartProfile(width, height, 15, "settings-15fps"),
                new StartProfile(1280, 720, 15, "720p-15fps"),
                new StartProfile(0, 0, 0, "driver-default")
            };
            for (int i = profiles.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (profiles[i].Width != profiles[j].Width || profiles[i].Height != profiles[j].Height || profiles[i].Fps != profiles[j].Fps) continue;
                    profiles.RemoveAt(i);
                    break;
                }
            }
            return profiles.ToArray();
        }

        private static WebCamTexture CreateTexture(string deviceName, StartProfile profile)
        {
            return profile.Width > 0 && profile.Height > 0 && profile.Fps > 0
                ? new WebCamTexture(deviceName, profile.Width, profile.Height, profile.Fps)
                : new WebCamTexture(deviceName);
        }

        private static string FormatRequest(StartProfile profile)
        {
            return profile.Width > 0 && profile.Height > 0 && profile.Fps > 0
                ? $"{profile.Width}x{profile.Height}@{profile.Fps}"
                : "driver-default";
        }

        private static void BindTexture(Binding binding, Material material)
        {
            if (binding == null || material == null) return;
            material.mainTexture = binding.Texture;
            if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", binding.Texture);
        }

        private static bool ShouldRecreate(Binding binding, WebCamTexture texture)
        {
            if (binding == null || texture == null) return false;
            if (texture.isPlaying && texture.width > 16 && texture.height > 16) return false;
            return binding.RetryCount >= 3 || Time.unscaledTime - binding.CreatedAt >= 3f;
        }

        private void Recreate(Binding binding, string reason)
        {
            if (binding == null) return;
            StartProfile[] profiles = BuildStartProfiles();
            int nextIndex = binding.ProfileIndex + 1;
            if (nextIndex >= profiles.Length)
            {
                binding.RetryCount = 0;
                binding.CreatedAt = Time.unscaledTime;
                _warning(
                    $"[webcam-fallback] exhausted device=\"{binding.DeviceName}\" reason={reason} " +
                    $"profiles={profiles.Length} keep={binding.ProfileIndex} isPlaying={binding.Texture?.isPlaying}");
                return;
            }

            var materials = new List<Material>(binding.Materials);
            WebCamTexture old = binding.Texture;
            _warning(
                $"[webcam-fallback] recreate device=\"{binding.DeviceName}\" reason={reason} " +
                $"fromProfile={binding.ProfileIndex} toProfile={nextIndex}");
            try
            {
                if (old != null)
                {
                    old.Stop();
                    UnityEngine.Object.Destroy(old);
                }
            }
            catch (Exception ex)
            {
                _warning($"[webcam-fallback] old texture destroy failed device=\"{binding.DeviceName}\": {ex.Message}");
            }
            _bindings[binding.DeviceName] = CreateBinding(binding.DeviceName, nextIndex, materials);
        }

        private void LogStatus(Binding binding, WebCamTexture texture)
        {
            if (binding == null || texture == null) return;
            PluginSettings settings = Settings;
            if (settings == null || !settings.VerboseLog) return;
            float statusInterval = Mathf.Max(0.25f, settings?.WebCamStatusLogIntervalSec ?? 1f);
            if (Time.unscaledTime >= binding.NextStatusLogTime)
            {
                binding.NextStatusLogTime = Time.unscaledTime + statusInterval;
                _info(
                    $"[webcam-status] device=\"{binding.DeviceName}\" listed={HasDevice(binding.DeviceName)} " +
                    $"requested={binding.RequestedWidth}x{binding.RequestedHeight}@{binding.RequestedFps} " +
                    $"isPlaying={texture.isPlaying} didUpdate={texture.didUpdateThisFrame} width={texture.width} height={texture.height} " +
                    $"rotation={texture.videoRotationAngle} mirrored={texture.videoVerticallyMirrored}");
            }
            if (!texture.isPlaying || texture.width <= 16 || texture.height <= 16) return;
            if (!binding.LoggedFirstFrame && texture.didUpdateThisFrame)
            {
                binding.LoggedFirstFrame = true;
                _info(
                    $"[webcam-frame] first-frame device=\"{binding.DeviceName}\" size={texture.width}x{texture.height} " +
                    $"rotation={texture.videoRotationAngle} mirrored={texture.videoVerticallyMirrored}");
            }
            float sampleInterval = Mathf.Max(0.5f, settings?.WebCamBlackSampleIntervalSec ?? 2f);
            if (Time.unscaledTime < binding.NextBlackSampleTime) return;
            binding.NextBlackSampleTime = Time.unscaledTime + sampleInterval;
            SampleBrightness(binding, texture);
        }

        private void SampleBrightness(Binding binding, WebCamTexture texture)
        {
            try
            {
                Color32[] pixels = texture.GetPixels32();
                if (pixels == null || pixels.Length == 0)
                {
                    _warning($"[webcam-sample] empty-pixels device=\"{binding.DeviceName}\"");
                    return;
                }
                int stride = Mathf.Max(1, pixels.Length / 64);
                int samples = 0;
                int min = 255;
                int max = 0;
                long sum = 0;
                for (int i = 0; i < pixels.Length; i += stride)
                {
                    Color32 color = pixels[i];
                    int luminance = (color.r * 299 + color.g * 587 + color.b * 114) / 1000;
                    if (luminance < min) min = luminance;
                    if (luminance > max) max = luminance;
                    sum += luminance;
                    samples++;
                }
                int average = samples > 0 ? (int)(sum / samples) : 0;
                string label = max <= 2 ? "near-black" : "ok";
                _info(
                    $"[webcam-sample] device=\"{binding.DeviceName}\" didUpdate={texture.didUpdateThisFrame} " +
                    $"samples={samples} avg={average} min={min} max={max} state={label}");
            }
            catch (Exception ex)
            {
                _warning($"[webcam-sample] failed device=\"{binding.DeviceName}\": {ex.Message}");
            }
        }

        private static bool HasDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null) return false;
            for (int i = 0; i < devices.Length; i++)
                if (string.Equals(devices[i].name, deviceName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal void Clear()
        {
            foreach (KeyValuePair<string, Binding> pair in _bindings)
            {
                try
                {
                    if (pair.Value?.Texture == null) continue;
                    pair.Value.Texture.Stop();
                    UnityEngine.Object.Destroy(pair.Value.Texture);
                }
                catch (Exception ex)
                {
                    _warning($"[webcam] destroy failed device=\"{pair.Key}\": {ex.Message}");
                }
            }
            _bindings.Clear();
            _updateKeys.Clear();
            _nextRetryTime = 0f;
            _info("[webcam] all textures destroyed");
        }

        public void Dispose() => Clear();
    }
}
