using System;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private string _lastVideoPath;

        private void SubscribeVideoEvents()
        {
            try
            {
                MainGameBlankMapAdd.Plugin.OnVideoLoaded += OnVideoLoaded;
                _log.Info("[VideoBridge] OnVideoLoaded 購読完了");
            }
            catch (Exception ex)
            {
                _log.Warn($"[VideoBridge] 購読失敗（MainGameBlankMapAddなし?）: {ex.Message}");
            }
        }

        private void UnsubscribeVideoEvents()
        {
            try
            {
                MainGameBlankMapAdd.Plugin.OnVideoLoaded -= OnVideoLoaded;
            }
            catch { }
        }

        private void OnVideoLoaded(string videoPath)
        {
            _lastVideoPath = videoPath;
            _log.Info($"[VideoBridge] 動画ロード: {videoPath}");

            // 動画に対応するプリセットを全ライトに適用
            string presetId = FindPresetForVideo(videoPath);
            if (string.IsNullOrEmpty(presetId)) return;

            foreach (var entry in _lightEntries)
                ApplyPresetToLight(entry.Settings, presetId, fromBeatSync: false, reason: "video-map");

            _log.Info($"[VideoBridge] プリセット適用: presetId={presetId}");
        }

        private string FindPresetForVideo(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath)) return null;

            foreach (var mapping in _settings.VideoPresetMappings)
            {
                if (string.Equals(mapping.VideoPath, videoPath,
                    StringComparison.OrdinalIgnoreCase))
                    return mapping.PresetId;
            }
            return null;
        }

        internal void AddVideoMapping(string videoPath, string presetId)
        {
            bool changed = false;
            // 同じパスがあれば上書き
            foreach (var m in _settings.VideoPresetMappings)
            {
                if (string.Equals(m.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(m.PresetId, presetId, StringComparison.Ordinal))
                    {
                        m.PresetId = presetId;
                        changed = true;
                    }
                    if (changed)
                        SaveSettingsNow("video-map-upsert");
                    return;
                }
            }
            _settings.VideoPresetMappings.Add(new VideoPresetMapping
            {
                VideoPath = videoPath,
                PresetId  = presetId
            });
            SaveSettingsNow("video-map-add");
        }

        internal void RemoveVideoMapping(int index)
        {
            if (index < 0 || index >= _settings.VideoPresetMappings.Count) return;
            _settings.VideoPresetMappings.RemoveAt(index);
            SaveSettingsNow("video-map-remove");
        }
    }
}
