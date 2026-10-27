using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        [DataContract]
        private sealed class SongBpmMapFileView
        {
            [DataMember(Order = 0)]
            public List<SongBpmMapEntryView> Items = new List<SongBpmMapEntryView>();
        }

        [DataContract]
        private sealed class SongBpmMapEntryView
        {
            [DataMember(Order = 0)]
            public string VideoPath = "";

            [DataMember(Order = 1)]
            public int Bpm = 0;
        }

        private readonly Dictionary<string, int> _beatSyncSavedSongBpmByKey =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _beatSyncSongMapPath;
        private DateTime _beatSyncSongMapLastWriteUtc = DateTime.MinValue;
        private float _nextBeatSyncSongMapPollTime = 0f;
        private bool _playbackBarHideLatchActive = false;
        private GUIStyle _playbackInfoTextStyle;
        private Texture2D _playbackInfoMaskTexture;

        private bool TryGetSavedBpmForCurrentVideo(out int savedBpm)
        {
            savedBpm = 0;
            RefreshBeatSyncSongMapCacheIfNeeded();

            string currentPath = CanonPlugins.Shared.VmdRhythmSource.TryRead(out string vmdPath, out _, out _) ? vmdPath : GetCurrentVideoPath();
            string key = NormalizeBeatSyncSongPathKey(currentPath);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return _beatSyncSavedSongBpmByKey.TryGetValue(key, out savedBpm);
        }

        private void DrawPlaybackInfoOverlay()
        {
            if (_settings == null || !_settings.EnablePlaybackBar)
                return;
            if (_mainVideoPlayer == null || _videoRoomRoot == null)
                return;

            if (!TryGetPlaybackBarPointerState(out Rect barRect, out bool mouseInTrigger, out bool mouseOverBar))
                return;
            if (_playbackBarHiddenByUser)
                return;

            bool shouldShow = mouseInTrigger || mouseOverBar || _playbackSeekDragging || _playbackVolumeDragging;
            if (!shouldShow)
                return;

            EnsurePlaybackInfoStyle();

            float pad = 6f;
            float rowH = 18f;
            float buttonH = 22f;
            float buttonW = Mathf.Max(36f, _settings.PlaybackBarButtonWidth);
            float loopButtonW = _settings.FolderPlayEnabled ? Mathf.Max(buttonW, 92f) : 0f;
            float hideButtonW = Mathf.Max(buttonW, 72f);
            float buttonsTotalW = buttonW * 3f + hideButtonW + pad * 3f;
            if (_settings.FolderPlayEnabled)
                buttonsTotalW += buttonW * 2f + loopButtonW + pad * 3f;

            float y = barRect.y + Mathf.Max(24f, (barRect.height - buttonH) * 0.5f - 4f);
            float textY = y + buttonH + 2f;
            if (textY + rowH > barRect.yMax - 14f)
                textY = y - rowH - 2f;

            // 既存のラベルを隠して、左右配置の新レイアウトを上描きする。
            var maskRect = new Rect(barRect.x + pad - 2f, textY - 1f, barRect.width - pad * 2f + 4f, rowH * 2f + 2f);
            GUI.DrawTexture(maskRect, _playbackInfoMaskTexture);

            string songText;
            if (_settings.FolderPlayEnabled && FolderFiles.Length > 0 && FolderIndex >= 0 && FolderIndex < FolderFiles.Length)
            {
                string title = Path.GetFileNameWithoutExtension(FolderFiles[FolderIndex]);
                songText = $"[{FolderIndex + 1}/{FolderFiles.Length}] {title}";
            }
            else
            {
                songText = GetCurrentVideoFileNameForPreset();
            }

            string bpmText = TryGetSavedBpmForCurrentVideo(out int savedBpm)
                ? $"BPM:{savedBpm}"
                : "BPM:未保存";

            double totalSec = ResolveTotalSeconds(_mainVideoPlayer);
            double currentSec = ResolveCurrentSeconds(_mainVideoPlayer, totalSec);
            string timeText = $"時間: {FormatSeconds(currentSec)} / {FormatSeconds(totalSec)}";

            float contentW = Mathf.Max(120f, barRect.width - pad * 2f);
            float rightW = Mathf.Clamp(contentW * 0.34f, 160f, 340f);
            float leftW = Mathf.Max(80f, contentW - rightW - 8f);
            float leftX = barRect.x + pad;
            float rightX = leftX + leftW + 8f;

            GUI.Label(
                new Rect(leftX, textY, leftW, rowH),
                $"曲: {songText} | {bpmText}",
                _playbackInfoTextStyle);
            GUI.Label(
                new Rect(rightX, textY, rightW, rowH),
                timeText,
                _playbackInfoTextStyle);
        }

        private bool ShouldSuppressPlaybackBarOnGuiByHideLatch()
        {
            if (!_playbackBarHideLatchActive)
                return false;
            if (!TryGetPlaybackBarPointerState(out _, out bool mouseInTrigger, out bool mouseOverBar))
            {
                _playbackBarHideLatchActive = false;
                return false;
            }

            if (!mouseInTrigger && !mouseOverBar)
            {
                _playbackBarHideLatchActive = false;
                return false;
            }

            // Hideボタン直後はカーソルがバー範囲内でも表示復帰させない。
            _playbackBarHiddenByUser = true;
            _playbackSeekDragging = false;
            _playbackVolumeDragging = false;
            return true;
        }

        private void UpdatePlaybackHideLatchPostGui()
        {
            if (!TryGetPlaybackBarPointerState(out _, out bool mouseInTrigger, out bool mouseOverBar))
            {
                _playbackBarHideLatchActive = false;
                return;
            }

            if (_playbackBarHiddenByUser && (mouseInTrigger || mouseOverBar))
            {
                _playbackBarHideLatchActive = true;
                return;
            }

            if (_playbackBarHideLatchActive && !mouseInTrigger && !mouseOverBar)
            {
                _playbackBarHideLatchActive = false;
            }
        }

        private bool TryGetPlaybackBarPointerState(
            out Rect barRect,
            out bool mouseInTrigger,
            out bool mouseOverBar)
        {
            barRect = default;
            mouseInTrigger = false;
            mouseOverBar = false;

            if (_settings == null || !_settings.EnablePlaybackBar)
                return false;
            if (_mainVideoPlayer == null || _videoRoomRoot == null)
                return false;

            float triggerPx = Mathf.Max(0f, _settings.PlaybackBarShowMouseBottomPx);
            float barHeight = GetPlaybackBarHeightPx();
            float marginX = Mathf.Max(0f, _settings.PlaybackBarMarginX);
            float barWidth = Mathf.Max(120f, Screen.width - marginX * 2f);
            barRect = new Rect(marginX, Screen.height - barHeight, barWidth, barHeight);

            var mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            mouseInTrigger = Input.mousePosition.y <= triggerPx;
            mouseOverBar = barRect.Contains(mouseGui);
            return true;
        }

        private void EnsurePlaybackInfoStyle()
        {
            if (_playbackInfoMaskTexture == null)
            {
                _playbackInfoMaskTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _playbackInfoMaskTexture.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 0.92f));
                _playbackInfoMaskTexture.Apply();
            }

            if (_playbackInfoTextStyle != null)
                return;

            _playbackInfoTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                clipping = TextClipping.Clip
            };
            _playbackInfoTextStyle.normal.textColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        }

        private void RefreshBeatSyncSongMapCacheIfNeeded()
        {
            if (Time.unscaledTime < _nextBeatSyncSongMapPollTime)
                return;

            _nextBeatSyncSongMapPollTime = Time.unscaledTime + 1f;

            string mapPath = ResolveBeatSyncSongMapPath();
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
            {
                _beatSyncSongMapLastWriteUtc = DateTime.MinValue;
                _beatSyncSavedSongBpmByKey.Clear();
                return;
            }

            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(mapPath);
            if (lastWriteUtc == _beatSyncSongMapLastWriteUtc)
                return;

            _beatSyncSongMapLastWriteUtc = lastWriteUtc;

            try
            {
                string json = File.ReadAllText(mapPath, Encoding.UTF8);
                var file = DeserializeSongBpmMap(json);

                _beatSyncSavedSongBpmByKey.Clear();
                var items = file?.Items;
                if (items != null)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        string key = NormalizeBeatSyncSongPathKey(item?.VideoPath);
                        if (!string.IsNullOrWhiteSpace(key))
                            _beatSyncSavedSongBpmByKey[key] = Mathf.Clamp(item.Bpm, 1, 999);
                    }
                }

                LogInfo($"[bpm-badge] song bpm map loaded entries={_beatSyncSavedSongBpmByKey.Count}");
            }
            catch (Exception ex)
            {
                _beatSyncSavedSongBpmByKey.Clear();
                LogWarn($"[bpm-badge] song bpm map read failed: {ex.Message}");
            }
        }

        private string ResolveBeatSyncSongMapPath()
        {
            if (!string.IsNullOrWhiteSpace(_beatSyncSongMapPath))
                return _beatSyncSongMapPath;

            try
            {
                string pluginsDir = Directory.GetParent(_pluginDir)?.FullName;
                if (string.IsNullOrWhiteSpace(pluginsDir))
                    return null;

                string rhythmMapPath = Path.Combine(pluginsDir, "MainGameRhythmSync", "SongBpmMap.json");
                _beatSyncSongMapPath = File.Exists(rhythmMapPath)
                    ? rhythmMapPath
                    : Path.Combine(pluginsDir, "MainGameBeatSyncSpeed", "SongBpmMap.json");
                return _beatSyncSongMapPath;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeBeatSyncSongPathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = NormalizeVideoPathInput(path);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (normalized.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                normalized = Uri.UnescapeDataString(normalized.Substring(8));
            else if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                normalized = Uri.UnescapeDataString(normalized.Substring(7));

            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (IsStreamUrl(normalized) || IsWebCamUrl(normalized))
                return null;

            try
            {
                string fullPath = Path.GetFullPath(normalized.Trim());
                return fullPath.Replace('/', '\\');
            }
            catch
            {
                return null;
            }
        }

        private static SongBpmMapFileView DeserializeSongBpmMap(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(SongBpmMapFileView));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as SongBpmMapFileView;
            }
        }

        [HarmonyPatch(typeof(Plugin), "OnGUI")]
        private static class OnGuiSavedBpmBadgePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Plugin __instance)
            {
                if (__instance == null)
                    return true;
                return !__instance.ShouldSuppressPlaybackBarOnGuiByHideLatch();
            }

            [HarmonyPostfix]
            private static void Postfix(Plugin __instance)
            {
                __instance?.UpdatePlaybackHideLatchPostGui();
            }
        }
    }
}
