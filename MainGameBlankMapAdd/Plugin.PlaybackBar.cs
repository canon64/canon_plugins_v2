// Uncomment to restore the experimental VRFx / VRFxUI playback-bar controls.
// #define SHOW_EXPERIMENTAL_VRFX_BAR
using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using BeatSyncSnapshot = MainGameBlankMapAdd.Integrations.IntegrationRegistry.BeatSyncSnapshot;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private bool _barDeferredPlayRequested { get => _playbackUi.DeferredPlayRequested; set => _playbackUi.DeferredPlayRequested = value; }
        private float _barDeferredPlayDeadline { get => _playbackUi.DeferredPlayDeadline; set => _playbackUi.DeferredPlayDeadline = value; }
        private VideoPlayer _barDeferredPlayPlayer { get => _playbackUi.DeferredPlayPlayer; set => _playbackUi.DeferredPlayPlayer = value; }

        private const float BeatSyncSnapshotBarPollSec = 0.25f;
        private const float ExternalUiSnapshotBarPollSec = 0.25f;
        private bool _beatSyncSnapshotBarCacheReady { get => _playbackUi.BeatSnapshotCacheReady; set => _playbackUi.BeatSnapshotCacheReady = value; }
        private float _nextBeatSyncSnapshotBarPollTime { get => _playbackUi.NextBeatSnapshotPollTime; set => _playbackUi.NextBeatSnapshotPollTime = value; }
        private bool _beatSyncSnapshotBarAvailable { get => _playbackUi.BeatSnapshotAvailable; set => _playbackUi.BeatSnapshotAvailable = value; }
        private BeatSyncSnapshot _beatSyncSnapshotBarCached { get => _playbackUi.BeatSnapshot; set => _playbackUi.BeatSnapshot = value; }
        private bool _externalUiSnapshotBarCacheReady { get => _playbackUi.ExternalSnapshotCacheReady; set => _playbackUi.ExternalSnapshotCacheReady = value; }
        private float _nextExternalUiSnapshotBarPollTime { get => _playbackUi.NextExternalSnapshotPollTime; set => _playbackUi.NextExternalSnapshotPollTime = value; }
        private bool _unifiedUiSnapshotBarAvailable { get => _playbackUi.UnifiedUiAvailable; set => _playbackUi.UnifiedUiAvailable = value; }
        private bool _unifiedUiSnapshotBarVisible { get => _playbackUi.UnifiedUiVisible; set => _playbackUi.UnifiedUiVisible = value; }
        private bool _afterimageSnapshotBarAvailable { get => _playbackUi.AfterimageAvailable; set => _playbackUi.AfterimageAvailable = value; }
        private bool _afterimageSnapshotBarEnabled { get => _playbackUi.AfterimageEnabled; set => _playbackUi.AfterimageEnabled = value; }
        private bool _vrScreenEffectSnapshotBarAvailable { get => _playbackUi.VrScreenEffectAvailable; set => _playbackUi.VrScreenEffectAvailable = value; }
        private bool _vrScreenEffectSnapshotBarEnabled { get => _playbackUi.VrScreenEffectEnabled; set => _playbackUi.VrScreenEffectEnabled = value; }
        private bool _vrScreenEffectUiSnapshotBarAvailable { get => _playbackUi.VrScreenEffectUiAvailable; set => _playbackUi.VrScreenEffectUiAvailable = value; }
        private bool _vrScreenEffectUiSnapshotBarVisible { get => _playbackUi.VrScreenEffectUiVisible; set => _playbackUi.VrScreenEffectUiVisible = value; }
        private bool _dollModeSnapshotBarAvailable { get => _playbackUi.DollModeAvailable; set => _playbackUi.DollModeAvailable = value; }
        private bool _dollModeSnapshotBarEnabled { get => _playbackUi.DollModeEnabled; set => _playbackUi.DollModeEnabled = value; }
        private bool _poseSnapshotBarAvailable { get => _playbackUi.PoseAvailable; set => _playbackUi.PoseAvailable = value; }
        private bool _poseSnapshotBarEnabled { get => _playbackUi.PoseEnabled; set => _playbackUi.PoseEnabled = value; }
        private bool _scenarioTextSnapshotBarAvailable { get => _playbackUi.ScenarioTextAvailable; set => _playbackUi.ScenarioTextAvailable = value; }
        private bool _scenarioTextAutoSendSnapshotBarEnabled { get => _playbackUi.ScenarioTextAutoSendEnabled; set => _playbackUi.ScenarioTextAutoSendEnabled = value; }

        private float GetPlaybackBarMinHeightPx()
        {
            return PlaybackTransportLayout.MinimumHeight(Screen.width - Mathf.Max(0, _settings.PlaybackBarMarginX) * 2, _playbackRoomControlsExpanded);
        }

        private bool TryGetBeatSyncSnapshotForBar(out BeatSyncSnapshot snapshot)
        {
            float now = Time.unscaledTime;
            if (!_beatSyncSnapshotBarCacheReady || now >= _nextBeatSyncSnapshotBarPollTime)
            {
                _beatSyncSnapshotBarCacheReady = true;
                _nextBeatSyncSnapshotBarPollTime = now + BeatSyncSnapshotBarPollSec;
                _beatSyncSnapshotBarAvailable = TryGetBeatSyncSnapshot(out BeatSyncSnapshot refreshedBeatSnapshot);
                _beatSyncSnapshotBarCached = refreshedBeatSnapshot;
            }

            if (_beatSyncSnapshotBarAvailable && _beatSyncSnapshotBarCached != null)
            {
                snapshot = _beatSyncSnapshotBarCached;
                return true;
            }

            snapshot = null;
            return false;
        }

        private void InvalidateBeatSyncSnapshotBarCache()
        {
            _beatSyncSnapshotBarCacheReady = false;
            _nextBeatSyncSnapshotBarPollTime = 0f;
            _beatSyncSnapshotBarAvailable = false;
            _beatSyncSnapshotBarCached = null;
        }

        private void RefreshExternalUiSnapshotBarCacheIfNeeded()
        {
            float now = Time.unscaledTime;
            if (_externalUiSnapshotBarCacheReady && now < _nextExternalUiSnapshotBarPollTime)
                return;

            _externalUiSnapshotBarCacheReady = true;
            _nextExternalUiSnapshotBarPollTime = now + ExternalUiSnapshotBarPollSec;

            _unifiedUiSnapshotBarAvailable = TryGetUnifiedUiVisible(out bool unifiedVisible);
            _unifiedUiSnapshotBarVisible = unifiedVisible;
            _afterimageSnapshotBarAvailable = TryGetAfterimageEnabled(out bool afterimageEnabled);
            _afterimageSnapshotBarEnabled = afterimageEnabled;
            _vrScreenEffectSnapshotBarAvailable = TryGetVrScreenEffectEnabled(out bool vrEffectEnabled);
            _vrScreenEffectSnapshotBarEnabled = vrEffectEnabled;
            _vrScreenEffectUiSnapshotBarAvailable = TryGetVrScreenEffectUiVisible(out bool vrEffectUiVisible);
            _vrScreenEffectUiSnapshotBarVisible = vrEffectUiVisible;
            _dollModeSnapshotBarAvailable = TryGetDollModeEnabled(out bool dollModeEnabled);
            _dollModeSnapshotBarEnabled = dollModeEnabled;
            _poseSnapshotBarAvailable = TryGetVfebPoseChangeEnabled(out bool poseEnabled);
            _poseSnapshotBarEnabled = poseEnabled;
            _scenarioTextSnapshotBarAvailable = TryGetVfebScenarioTextAutoSendEnabled(out bool scenarioTextEnabled);
            _scenarioTextAutoSendSnapshotBarEnabled = scenarioTextEnabled;
        }

        private void InvalidateExternalUiSnapshotBarCache()
        {
            _externalUiSnapshotBarCacheReady = false;
            _nextExternalUiSnapshotBarPollTime = 0f;
            _unifiedUiSnapshotBarAvailable = false;
            _unifiedUiSnapshotBarVisible = false;
            _afterimageSnapshotBarAvailable = false;
            _afterimageSnapshotBarEnabled = false;
            _vrScreenEffectSnapshotBarAvailable = false;
            _vrScreenEffectSnapshotBarEnabled = false;
            _vrScreenEffectUiSnapshotBarAvailable = false;
            _vrScreenEffectUiSnapshotBarVisible = false;
            _dollModeSnapshotBarAvailable = false;
            _dollModeSnapshotBarEnabled = false;
            _poseSnapshotBarAvailable = false;
            _poseSnapshotBarEnabled = false;
            _scenarioTextSnapshotBarAvailable = false;
            _scenarioTextAutoSendSnapshotBarEnabled = false;
        }

        private void RequestDeferredPlayFromBar(VideoPlayer player, float timeoutSec = 8f)
        {
            _barDeferredPlayRequested = player != null;
            _barDeferredPlayPlayer = player;
            _barDeferredPlayDeadline = Time.unscaledTime + Mathf.Max(1f, timeoutSec);
        }

        private void ClearDeferredPlayFromBar()
        {
            _barDeferredPlayRequested = false;
            _barDeferredPlayPlayer = null;
            _barDeferredPlayDeadline = 0f;
        }

        private void TickDeferredPlayFromBar()
        {
            if (ExternalMediaBlocksPlayback) { ClearDeferredPlayFromBar(); return; }
            if (!_barDeferredPlayRequested)
                return;

            VideoPlayer player = _barDeferredPlayPlayer;
            if (player == null)
            {
                ClearDeferredPlayFromBar();
                return;
            }

            if (!player.isPrepared)
            {
                if (Time.unscaledTime >= _barDeferredPlayDeadline)
                {
                    LogWarn("video play deferred timeout: prepare not completed");
                    ClearDeferredPlayFromBar();
                }
                return;
            }

            ClearDeferredPlayFromBar();
            if (IsAstralModeSuppressed)
            {
                ForceHoldVideoAtStart(player, player.url, "astral-suppressed-bar-deferred");
                LogInfo("video play blocked by AstralMode (bar-deferred)");
                return;
            }
            ConfigureVideoAudio(player, true);
            player.Play();
            LogInfo("video play (bar-deferred)");
            LogAudioDiagnosticsSnapshot("bar-play-deferred", includeStoppedSources: true);
        }

        private void PlayFromBar(VideoPlayer player)
        {
            if (ExternalMediaBlocksPlayback) return;
            if (player == null)
                return;
            if (IsAstralModeSuppressed)
            {
                ForceHoldVideoAtStart(player, player.url, "astral-suppressed-bar");
                LogInfo("video play blocked by AstralMode (bar)");
                return;
            }

            ConfigureVideoAudio(player, true);

            bool atHead = false;
            try
            {
                atHead = !player.isPlaying &&
                         player.isPrepared &&
                         player.frame <= 0L &&
                         player.time <= 0.001d;
            }
            catch
            {
                atHead = false;
            }

            if (atHead)
            {
                try { player.Stop(); } catch { }
                try { player.Prepare(); } catch { }
                RequestDeferredPlayFromBar(player);
                LogInfo("video play requested (bar, restart-prepare)");
                LogAudioDiagnosticsSnapshot("bar-play-request-reprepare", includeStoppedSources: true);
                return;
            }

            if (!player.isPrepared)
            {
                try { player.Prepare(); } catch { }
                RequestDeferredPlayFromBar(player);
                LogInfo("video play requested (bar, prepare)");
                LogAudioDiagnosticsSnapshot("bar-play-request-prepare", includeStoppedSources: true);
                return;
            }

            player.Play();
            LogInfo("video play (bar)");
            LogAudioDiagnosticsSnapshot("bar-play", includeStoppedSources: true);
        }

        private static string FormatRoomNumeric(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryParseRoomNumeric(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static int GetNextCubeFaceTileCount(int current)
        {
            switch (current)
            {
                case 1: return 4;
                case 4: return 9;
                case 9: return 16;
                case 16: return 25;
                default: return 1;
            }
        }

        private void ApplyCubeFaceTileCountFromBar(int nextCount)
        {
            if (nextCount != 1 && nextCount != 4 && nextCount != 9 && nextCount != 16 && nextCount != 25)
                nextCount = 1;
            if (_settings == null || _settings.CubeFaceTileCount == nextCount)
                return;

            _settings.CubeFaceTileCount = nextCount;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgCubeFaceTileCount != null)
                    _cfgCubeFaceTileCount.Value = nextCount;
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);

            if (LastReservedMap != null && LastReservedMap.mapRoot != null)
            {
                if (TryRefreshCubeSurfaceTilesInPlace(out string retileDetail))
                {
                    LogInfo($"cube face tile count changed count={nextCount} (bar, in-place: {retileDetail})");
                    return;
                }

                ForceReapplyCurrentMap();
                LogInfo($"cube face tile count changed count={nextCount} (bar, map refreshed fallback)");
                return;
            }

            LogInfo($"cube face tile count changed count={nextCount} (bar)");
        }

        private static string NormalizeFolderPathForList(string value)
        {
            string normalized = NormalizeVideoPathInput(NormalizeString(value));
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            normalized = normalized.Replace('/', '\\');
            while (normalized.Length > 3 && normalized.EndsWith("\\", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }

        private bool EnsureFolderPlayPathListConsistency()
        {
            if (_settings == null)
                return false;

            bool changed = false;
            if (_settings.FolderPlayPaths == null)
            {
                _settings.FolderPlayPaths = new System.Collections.Generic.List<string>();
                changed = true;
            }

            var cleaned = new System.Collections.Generic.List<string>();
            for (int i = 0; i < _settings.FolderPlayPaths.Count; i++)
            {
                string normalized = NormalizeFolderPathForList(_settings.FolderPlayPaths[i]);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    changed = true;
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < cleaned.Count; j++)
                {
                    if (!string.Equals(cleaned[j], normalized, StringComparison.OrdinalIgnoreCase))
                        continue;
                    exists = true;
                    break;
                }

                if (exists)
                {
                    changed = true;
                    continue;
                }

                cleaned.Add(normalized);
            }

            if (cleaned.Count != _settings.FolderPlayPaths.Count)
                changed = true;
            else
            {
                for (int i = 0; i < cleaned.Count; i++)
                {
                    if (string.Equals(cleaned[i], _settings.FolderPlayPaths[i], StringComparison.Ordinal))
                        continue;
                    changed = true;
                    break;
                }
            }

            if (changed)
                _settings.FolderPlayPaths = cleaned;

            string currentPath = NormalizeFolderPathForList(_settings.FolderPlayPath);
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                bool existsInList = false;
                for (int i = 0; i < _settings.FolderPlayPaths.Count; i++)
                {
                    if (!string.Equals(_settings.FolderPlayPaths[i], currentPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    existsInList = true;
                    break;
                }

                if (!existsInList)
                {
                    _settings.FolderPlayPaths.Add(currentPath);
                    changed = true;
                }
            }
            else if (_settings.FolderPlayPaths.Count > 0)
            {
                _settings.FolderPlayPath = _settings.FolderPlayPaths[0];
                changed = true;
            }

            string normalizedCurrent = NormalizeFolderPathForList(_settings.FolderPlayPath);
            if (!string.Equals(_settings.FolderPlayPath, normalizedCurrent, StringComparison.Ordinal))
            {
                _settings.FolderPlayPath = normalizedCurrent;
                changed = true;
            }

            return changed;
        }

        private void SaveFolderPlayStateFromBar(bool rescanNow, string logMessage)
        {
            if (_settings == null)
                return;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgFolderPlayPath != null)
                    _cfgFolderPlayPath.Value = NormalizeVideoPathInput(_settings.FolderPlayPath);
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);

            if (rescanNow)
                ForceFolderRescan();

            if (!string.IsNullOrWhiteSpace(logMessage))
                LogInfo(logMessage);
        }

        private static string GetSortModeDisplayLabel(string mode)
        {
            if (string.Equals(mode, "Date", StringComparison.OrdinalIgnoreCase)) return "Sort:Date";
            if (string.Equals(mode, "Random", StringComparison.OrdinalIgnoreCase)) return "Sort:Random";
            return "Sort:Name";
        }

        private static string GetNextSortMode(string current)
        {
            if (string.Equals(current, "Name", StringComparison.OrdinalIgnoreCase)) return "Date";
            if (string.Equals(current, "Date", StringComparison.OrdinalIgnoreCase)) return "Random";
            return "Name";
        }

        private void CycleFolderSortModeFromBar()
        {
            if (_settings == null) return;

            string currentVideo = (FolderIndex >= 0 && FolderIndex < FolderFiles.Length)
                ? FolderFiles[FolderIndex]
                : null;

            string next = GetNextSortMode(_settings.FolderPlaySortMode);
            _settings.FolderPlaySortMode = next;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgFolderPlaySortMode != null)
                    _cfgFolderPlaySortMode.Value = next;
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);
            ForceFolderRescan();

            // 再生中の動画のインデックスを維持
            if (currentVideo != null && FolderFiles.Length > 0)
            {
                for (int i = 0; i < FolderFiles.Length; i++)
                {
                    if (string.Equals(FolderFiles[i], currentVideo, StringComparison.OrdinalIgnoreCase))
                    {
                        FolderIndex = i;
                        break;
                    }
                }
            }

            LogInfo($"folder play: sort mode changed to {next} (bar)");
        }

        private void SetCurrentFolderPlayPathFromBar(string folderPath)
        {
            if (_settings == null)
                return;

            string normalized = NormalizeFolderPathForList(folderPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            string current = NormalizeFolderPathForList(_settings.FolderPlayPath);
            bool changed = !string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase);
            _settings.FolderPlayPath = normalized;
            EnsureFolderPlayPathListConsistency();

            if (!changed)
                return;

            SaveFolderPlayStateFromBar(
                rescanNow: true,
                logMessage: $"folder play: selected folder path={normalized}");
        }

        private void AddFolderPlayPathFromBar(string selectedPath)
        {
            if (_settings == null)
                return;

            string normalized = NormalizeFolderPathForList(selectedPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            EnsureFolderPlayPathListConsistency();

            bool added = false;
            bool exists = false;
            for (int i = 0; i < _settings.FolderPlayPaths.Count; i++)
            {
                if (!string.Equals(_settings.FolderPlayPaths[i], normalized, StringComparison.OrdinalIgnoreCase))
                    continue;
                exists = true;
                break;
            }
            if (!exists)
            {
                _settings.FolderPlayPaths.Add(normalized);
                added = true;
            }

            string current = NormalizeFolderPathForList(_settings.FolderPlayPath);
            bool selectedChanged = !string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase);
            if (selectedChanged)
                _settings.FolderPlayPath = normalized;

            if (!added && !selectedChanged)
                return;

            SaveFolderPlayStateFromBar(
                rescanNow: selectedChanged,
                logMessage: $"folder play: {(added ? "added" : "selected")} folder path={normalized}");
        }

        private static string GetFolderDisplayNameForBar(string folderPath)
        {
            string normalized = NormalizeFolderPathForList(folderPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return "(none)";

            string name = Path.GetFileName(normalized.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(name))
                return normalized;
            return name;
        }

        private static string GetVideoDisplayNameForBar(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                return "(none)";

            string title = Path.GetFileNameWithoutExtension(videoPath);
            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileName(videoPath);
            if (string.IsNullOrWhiteSpace(title))
                return "(none)";
            return title;
        }

        private bool DrawRoomNumericInput(
            string controlName,
            Rect rect,
            ref string buffer,
            float currentValue,
            out float parsedValue)
        {
            string focused = GUI.GetNameOfFocusedControl();
            bool isFocused = string.Equals(focused, controlName, StringComparison.Ordinal);
            if (!isFocused)
                buffer = FormatRoomNumeric(currentValue);

            GUI.SetNextControlName(controlName);
            string next = GUI.TextField(rect, buffer ?? string.Empty);
            if (!string.Equals(next, buffer, StringComparison.Ordinal))
                buffer = next;

            if (TryParseRoomNumeric(buffer, out parsedValue))
                return true;

            if (!isFocused)
                buffer = FormatRoomNumeric(currentValue);

            parsedValue = currentValue;
            return false;
        }

        private void OnGUI()
        {
            // フォルダ切り替えフェードオーバーレイ
            if (_fadePhase != FadePhase.None)
            {
                float alpha = _fadePhase == FadePhase.ToBlack
                    ? Mathf.Clamp01(_fadeElapsed / Mathf.Max(0.001f, _fadePhaseDuration))
                    : Mathf.Clamp01(1f - _fadeElapsed / Mathf.Max(0.001f, _fadePhaseDuration));
                var prevColor = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, alpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _uiResources.BlackTexture);
                GUI.color = prevColor;
            }

            ReadVmdBar();
            if (!UnityEngine.Application.isFocused) { HideUiBlocker(); return; }
            if (_playbackBarCtrlHidden) { HideUiBlocker(); return; }
            if (_settings == null || !_settings.EnablePlaybackBar) { HideUiBlocker(); return; }
            if (_videoRoomRoot == null) { HideUiBlocker(); return; }

            float triggerPx = Mathf.Max(0f, _settings.PlaybackBarShowMouseBottomPx);
            // 上段の部屋操作スライダー群 + ボタン群 + 下段再生スライダー群の3段構成を確保
            float barHeight = GetPlaybackBarHeightPx();
            float marginX = Mathf.Max(0f, _settings.PlaybackBarMarginX);
            float buttonW = Screen.width < 1100 ? 48f : Mathf.Max(36f, _settings.PlaybackBarButtonWidth);

            float barWidth = Mathf.Max(120f, Screen.width - marginX * 2f);
            var barRect = new Rect(marginX, Screen.height - barHeight, barWidth, barHeight);

            var mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool mouseInTrigger = Input.mousePosition.y <= triggerPx;
            bool mouseOverBar = barRect.Contains(mouseGui);
            bool dropdownOpen = (_actorCoordinatesOpen && _actorCoordinatesList.Contains(mouseGui)) || _vmdGroupOpen || _vmdEntryOpen || (_settings.FolderPlayEnabled && (_folderDropdownOpen || _videoDropdownOpen)) || _reverbDropdownOpen || _roomOverlayDropdownOpen;
            bool holdingMouse = Input.GetMouseButton(0);

            if (_playbackBarHiddenByUser && (mouseOverBar || mouseInTrigger || dropdownOpen))
            {
                _playbackBarHiddenByUser = false;
            }

            if (_playbackVolumeDragging && !holdingMouse)
            {
                _playbackVolumeDragging = false;
                if (!_vmdBarMode) CommitPlaybackBarVolume();
            }
            if (_playbackGainDragging && !holdingMouse)
            {
                _playbackGainDragging = false;
                CommitPlaybackBarVideoGain();
            }
            if (_playbackBarHiddenByUser &&
                !_playbackSeekDragging &&
                !_playbackVolumeDragging &&
                !_playbackGainDragging)
            { HideUiBlocker(); return; }

            UpdateEmbeddedBarDrag(mouseGui);
            bool shouldShow = mouseInTrigger || mouseOverBar || dropdownOpen || _embeddedBarDragging ||
                _playbackSeekDragging || _playbackVolumeDragging || _playbackGainDragging;
            if (!shouldShow) { HideUiBlocker(); return; }

            string hoveredHelpText = null;

            void TrackHelp(Rect rect, string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;
                if (Event.current != null && rect.Contains(Event.current.mousePosition))
                    hoveredHelpText = text;
            }

            bool HelpButton(Rect rect, string label, string help)
            {
                TrackHelp(rect, help);
                return GUI.Button(rect, new GUIContent(label, help));
            }

            bool HelpButtonContent(Rect rect, GUIContent content)
            {
                TrackHelp(rect, content?.tooltip);
                return GUI.Button(rect, content);
            }

            bool HelpToggle(Rect rect, bool value, string label, string help)
            {
                TrackHelp(rect, help);
                return GUI.Toggle(rect, value, new GUIContent(label, help));
            }

            float HelpSlider(Rect rect, float value, float leftValue, float rightValue, string help)
            {
                TrackHelp(rect, help);
                int before = GUIUtility.hotControl;
                float result = GUI.HorizontalSlider(rect, value, leftValue, rightValue);
                if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != before)
                    _playbackSliderHotControl = GUIUtility.hotControl;
                return result;
            }

            if (EnsureFolderPlayPathListConsistency())
                SaveFolderPlayStateFromBar(rescanNow: false, logMessage: null);

            VideoPlayer player = _mainVideoPlayer;
            double totalSec = ResolveTotalSeconds(player);
            double currentSec = ResolveCurrentSeconds(player, totalSec);

            GUI.Box(barRect, GUIContent.none);

            float pad = 6f;
            float rowH = 20f;
            float buttonH = 22f;

            float loopButtonW = _settings.FolderPlayEnabled ? Mathf.Max(buttonW, 92f) : 0f;
            float sortButtonW = _settings.FolderPlayEnabled ? Mathf.Max(buttonW, 108f) : 0f;
            float addFolderButtonW = _settings.FolderPlayEnabled ? Mathf.Max(buttonW, 98f) : 0f;
            float folderDropdownW = _settings.FolderPlayEnabled ? Mathf.Max(buttonW, 142f) : 0f;
            float videoDropdownW = _settings.FolderPlayEnabled ? Mathf.Max(buttonW, 150f) : 0f;
            float tileButtonW = Mathf.Max(buttonW, 108f);
            // HandySyncが入っている時だけ、機器の開始/停止ボタン2つ分を確保する。
            bool showHandyButtons = IsHandySyncAvailable();
            float buttonsTotalW = buttonW * 3f + tileButtonW + pad * 3f + 124f;

            if (_settings.FolderPlayEnabled)
            {
                buttonsTotalW += buttonW * 2f + loopButtonW + sortButtonW + addFolderButtonW + folderDropdownW + videoDropdownW + pad * 7f;
            }

            float sideGap = 10f;
            float sideWidth = (barRect.width - buttonsTotalW - sideGap * 2f - pad * 2f) * 0.5f;
            if (sideWidth < 0f)
                sideWidth = 0f;

            float leftX = barRect.x + pad;
            float centerX = leftX + 28f;
            float rightX = centerX + buttonsTotalW + sideGap;
            float leftW = Mathf.Max(0f, centerX - sideGap - leftX);
            float rightW = Mathf.Max(0f, barRect.xMax - pad - rightX);

            bool showRoomControls = _playbackRoomControlsExpanded;
            float layoutSectionH = 64f;
            float integrationSectionH = 146f;
            float slideshowSectionH = 138f;
            float overlaySectionH = 122f;
            float controlsGapY = 4f;
            // Reserve the transport at the bottom; only the upper settings viewport scrolls.
            float expandedHeight = showRoomControls ? Mathf.Max(0f, barRect.height - PlaybackTransportLayout.MinimumHeight(barRect.width, false)) : 0f;
            float y = barRect.y + expandedHeight + 8f;

            float roomButtonX = DrawClothingTargets(new Rect(barRect.x + 6, y, barRect.width - 12, 22)) + 8f;
            DrawRoomOverlayButton(new Rect(roomButtonX, y, 185f, 22f));
            float presetControlsX = roomButtonX + 185f + 10f;
            if (barRect.width >= 1270f)
                DrawClubLightsPlaybackControls(new Rect(presetControlsX, barRect.y, barRect.xMax - presetControlsX, barRect.height), y);
            else
                DrawClubLightsPlaybackControls(barRect, y + 24f);
            y += PlaybackTransportLayout.TopControlsHeight(barRect.width);

            void DrawEmbeddedRoom(float roomWidth)
            {
                float controlsTopY = 4f;
                float sectionGapX = 8f;
                float controlsLeftX = pad;
                float controlsWidth = Mathf.Max(320f, roomWidth - pad * 2f);
                float saveButtonsW = 220f;

                float sectionPoolW = controlsWidth - saveButtonsW - sectionGapX * 3f;
                if (sectionPoolW < 320f)
                    sectionPoolW = 320f;

                float sizeSectionW = Mathf.Clamp(sectionPoolW * 0.18f, 120f, 180f);
                float posSectionW = (sectionPoolW - sizeSectionW - sectionGapX) * 0.5f;
                float rotSectionW = sectionPoolW - sizeSectionW - sectionGapX - posSectionW;
                posSectionW = Mathf.Max(160f, posSectionW);
                rotSectionW = Mathf.Max(160f, rotSectionW);

                float sizeX = controlsLeftX;
                float posX = sizeX + sizeSectionW + sectionGapX;
                float rotX = posX + posSectionW + sectionGapX;
                float saveX = rotX + rotSectionW + sectionGapX;

                GUI.Box(new Rect(sizeX, controlsTopY, sizeSectionW, layoutSectionH), "SIZE");
                GUI.Box(new Rect(posX, controlsTopY, posSectionW, layoutSectionH), "POSITION");
                GUI.Box(new Rect(rotX, controlsTopY, rotSectionW, layoutSectionH), "ROTATION");
                GUI.Box(
                    new Rect(saveX, controlsTopY, saveButtonsW, layoutSectionH),
                    new GUIContent("AUDIO / SAVE", "音量補正と保存（フォルダ/動画個別）"));

                float roomScaleCurrent = _videoRoomRoot != null
                    ? Mathf.Clamp(_videoRoomRoot.transform.localScale.x, 0.25f, 4f)
                    : Mathf.Clamp(_playbackRoomScale, 0.25f, 4f);
                if (_videoRoomRoot != null)
                    _playbackRoomScale = roomScaleCurrent;

                float sizeLabelY = controlsTopY + 16f;
                float sizeSliderY = controlsTopY + 31f;
                float sizeInputW = 58f;
                float sizeLabelW = 34f;
                float sizeBodyX = sizeX + 8f;
                var sizeInputRect = new Rect(sizeX + sizeSectionW - 8f - sizeInputW, sizeLabelY, sizeInputW, 18f);
                float sizeSliderX = sizeBodyX + sizeLabelW + 2f;
                float sizeSliderW = Mathf.Max(18f, sizeInputRect.x - 4f - sizeSliderX);

                GUI.Label(new Rect(sizeBodyX, sizeLabelY, sizeLabelW, rowH), "Scale");
                if (DrawRoomNumericInput("RoomScaleInput", sizeInputRect, ref _roomScaleInput, roomScaleCurrent, out float typedScale))
                {
                    float clampedTypedScale = Mathf.Clamp(typedScale, 0.25f, 4f);
                    if (Mathf.Abs(clampedTypedScale - roomScaleCurrent) > 0.0001f)
                    {
                        roomScaleCurrent = clampedTypedScale;
                        _playbackRoomScale = clampedTypedScale;
                        if (_videoRoomRoot != null)
                            _videoRoomRoot.transform.localScale = Vector3.one * clampedTypedScale;
                        ConfigureVideoRoomAudioSource(_videoRoomAudioSource);
                    }
                }

                var roomScaleSliderRect = new Rect(sizeSliderX, sizeSliderY, sizeSliderW, 12f);
                float nextRoomScale = HelpSlider(
                    roomScaleSliderRect,
                    roomScaleCurrent,
                    0.25f,
                    4f,
                    "動画ルーム全体のサイズ");
                if (Mathf.Abs(nextRoomScale - roomScaleCurrent) > 0.0001f)
                {
                    _playbackRoomScale = nextRoomScale;
                    _roomScaleInput = FormatRoomNumeric(nextRoomScale);
                    if (_videoRoomRoot != null)
                        _videoRoomRoot.transform.localScale = Vector3.one * nextRoomScale;
                    ConfigureVideoRoomAudioSource(_videoRoomAudioSource);
                }

                float axisGap = 6f;
                float posAxisW = (posSectionW - 16f - axisGap * 2f) / 3f;
                float rotAxisW = (rotSectionW - 16f - axisGap * 2f) / 3f;
                float axisLabelY = controlsTopY + 16f;
                float axisSliderY = controlsTopY + 31f;
                float axisNameW = 12f;

                bool roomOffsetChanged = false;
                for (int axis = 0; axis < 3; axis++)
                {
                    float px = posX + 8f + axis * (posAxisW + axisGap);
                    string axisName = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
                    float current = axis == 0
                        ? _settings.VideoRoomOffsetX
                        : axis == 1
                            ? _settings.VideoRoomOffsetY
                            : _settings.VideoRoomOffsetZ;
                    float min = axis == 1 ? -10f : -20f;
                    float max = axis == 1 ? 10f : 20f;
                    float inputW = Mathf.Max(18f, posAxisW - axisNameW - 1f);
                    GUI.Label(new Rect(px, axisLabelY, axisNameW, rowH), axisName);
                    var inputRect = new Rect(px + axisNameW + 1f, axisLabelY, inputW, 18f);
                    if (DrawRoomNumericInput($"RoomPosInput{axis}", inputRect, ref _roomPosInputs[axis], current, out float typed))
                    {
                        float clampedTyped = Mathf.Clamp(typed, min, max);
                        if (Mathf.Abs(clampedTyped - current) > 0.0001f)
                        {
                            current = clampedTyped;
                            if (axis == 0) _settings.VideoRoomOffsetX = clampedTyped;
                            else if (axis == 1) _settings.VideoRoomOffsetY = clampedTyped;
                            else _settings.VideoRoomOffsetZ = clampedTyped;
                            roomOffsetChanged = true;
                        }
                    }

                    var roomPosSliderRect = new Rect(px, axisSliderY, posAxisW, 12f);
                    float next = HelpSlider(
                        roomPosSliderRect,
                        current,
                        min,
                        max,
                        $"動画ルーム位置 {axisName} 軸");
                    if (Mathf.Abs(next - current) <= 0.0001f)
                        continue;

                    if (axis == 0) _settings.VideoRoomOffsetX = next;
                    else if (axis == 1) _settings.VideoRoomOffsetY = next;
                    else _settings.VideoRoomOffsetZ = next;
                    _roomPosInputs[axis] = FormatRoomNumeric(next);
                    roomOffsetChanged = true;
                }

                if (roomOffsetChanged && _videoRoomRoot != null)
                {
                    EnsureFemaleCharaRef();
                    if (_femaleChara != null)
                    {
                        Vector3 fp = _femaleChara.transform.position;
                        float baseY = _hasFemaleBaseY ? _femaleBaseY : fp.y;
                        _videoRoomRoot.transform.position = new Vector3(
                            fp.x + _settings.VideoRoomOffsetX,
                            baseY + _settings.VideoRoomOffsetY,
                            fp.z + _settings.VideoRoomOffsetZ);
                    }
                }

                bool roomRotationChanged = false;
                for (int axis = 0; axis < 3; axis++)
                {
                    float rx = rotX + 8f + axis * (rotAxisW + axisGap);
                    string axisName = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
                    float current = axis == 0
                        ? _settings.VideoRoomRotationX
                        : axis == 1
                            ? _settings.VideoRoomRotationY
                            : _settings.VideoRoomRotationZ;
                    float inputW = Mathf.Max(18f, rotAxisW - axisNameW - 1f);
                    GUI.Label(new Rect(rx, axisLabelY, axisNameW, rowH), axisName);
                    var inputRect = new Rect(rx + axisNameW + 1f, axisLabelY, inputW, 18f);
                    if (DrawRoomNumericInput($"RoomRotInput{axis}", inputRect, ref _roomRotInputs[axis], current, out float typed))
                    {
                        float clampedTyped = Mathf.Clamp(typed, 0f, 360f);
                        if (Mathf.Abs(clampedTyped - current) > 0.0001f)
                        {
                            current = clampedTyped;
                            if (axis == 0) _settings.VideoRoomRotationX = clampedTyped;
                            else if (axis == 1) _settings.VideoRoomRotationY = clampedTyped;
                            else _settings.VideoRoomRotationZ = clampedTyped;
                            roomRotationChanged = true;
                        }
                    }

                    var roomRotSliderRect = new Rect(rx, axisSliderY, rotAxisW, 12f);
                    float next = HelpSlider(
                        roomRotSliderRect,
                        current,
                        0f,
                        360f,
                        $"動画ルーム回転 {axisName} 軸");
                    if (Mathf.Abs(next - current) <= 0.0001f)
                        continue;

                    if (axis == 0) _settings.VideoRoomRotationX = next;
                    else if (axis == 1) _settings.VideoRoomRotationY = next;
                    else _settings.VideoRoomRotationZ = next;
                    _roomRotInputs[axis] = FormatRoomNumeric(next);
                    roomRotationChanged = true;
                }

                if (roomRotationChanged && _videoRoomRoot != null)
                {
                    _videoRoomRoot.transform.rotation = Quaternion.Euler(
                        _settings.VideoRoomRotationX,
                        _settings.VideoRoomRotationY,
                        _settings.VideoRoomRotationZ);
                }

                float audioLabelY = controlsTopY + 16f;
                float audioSliderY = controlsTopY + 31f;
                float audioLabelW = 34f;
                float audioInputW = 54f;
                float audioBodyX = saveX + 8f;
                float currentAudioGain = ResolveCurrentVideoAudioGain();
                var gainInputRect = new Rect(saveX + saveButtonsW - 8f - audioInputW, audioLabelY, audioInputW, 18f);
                float gainSliderX = audioBodyX + audioLabelW + 2f;
                float gainSliderW = Mathf.Max(18f, gainInputRect.x - 4f - gainSliderX);
                var gainSliderRect = new Rect(gainSliderX, audioSliderY, gainSliderW, 12f);

                GUI.Label(
                    new Rect(audioBodyX, audioLabelY, audioLabelW, rowH),
                    new GUIContent("Gain", "動画音声の追加ゲイン倍率（0.1〜6.0）"));
                if (DrawRoomNumericInput("VideoGainInput", gainInputRect, ref _videoGainInput, currentAudioGain, out float typedGain))
                {
                    float clampedTypedGain = Mathf.Clamp(typedGain, 0.1f, 6f);
                    if (Mathf.Abs(clampedTypedGain - currentAudioGain) > 0.0001f)
                        ApplyPlaybackBarVideoGain(clampedTypedGain, persist: false);
                }

                if (Event.current != null &&
                    Event.current.type == EventType.MouseDown &&
                    gainSliderRect.Contains(Event.current.mousePosition))
                {
                    _playbackGainDragging = true;
                }

                float nextGain = HelpSlider(gainSliderRect, currentAudioGain, 0.1f, 6f, "動画音声の追加ゲイン倍率");
                if (Mathf.Abs(nextGain - currentAudioGain) > 0.0001f)
                    ApplyPlaybackBarVideoGain(nextGain, persist: false);

                float saveButtonGap = 4f;
                float savePadX = 8f;
                float saveButtonW = (saveButtonsW - savePadX * 2f - saveButtonGap * 3f) * 0.25f;
                float saveButtonY = controlsTopY + 43f;
                float saveButtonH = 18f;
                float saveRoomFolderX = saveX + savePadX;
                float saveRoomVideoX = saveRoomFolderX + saveButtonW + saveButtonGap;
                float saveGainFolderX = saveRoomVideoX + saveButtonW + saveButtonGap;
                float saveGainVideoX = saveGainFolderX + saveButtonW + saveButtonGap;

                if (HelpButtonContent(
                    new Rect(saveRoomFolderX, saveButtonY, saveButtonW, saveButtonH),
                    new GUIContent("RoomF", "部屋のサイズ/位置/回転をフォルダ設定に保存")))
                {
                    SaveFolderRoomLayoutProfileFromBar();
                }
                if (HelpButtonContent(
                    new Rect(saveRoomVideoX, saveButtonY, saveButtonW, saveButtonH),
                    new GUIContent("RoomV", "部屋のサイズ/位置/回転を動画個別設定に保存")))
                {
                    SaveVideoRoomLayoutProfileFromBar();
                }
                if (HelpButtonContent(
                    new Rect(saveGainFolderX, saveButtonY, saveButtonW, saveButtonH),
                    new GUIContent("GainF", "動画音声ゲインをフォルダ設定に保存")))
                {
                    SaveFolderAudioGainProfileFromBar();
                }
                if (HelpButtonContent(
                    new Rect(saveGainVideoX, saveButtonY, saveButtonW, saveButtonH),
                    new GUIContent("GainV", "動画音声ゲインを動画個別設定に保存")))
                {
                    SaveVideoAudioGainProfileFromBar();
                }

                float integrationTopY = controlsTopY + layoutSectionH + controlsGapY;
                float beatSyncX = controlsLeftX;
                float beatSyncW = controlsWidth;
                GUI.Box(new Rect(beatSyncX, integrationTopY, beatSyncW, integrationSectionH), "BEAT SYNC");

                float row1Y = integrationTopY + 16f;

                if (TryGetBeatSyncSnapshotForBar(out BeatSyncSnapshot beatSnapshot))
                {
                    var nextBeat = new BeatSyncSnapshot
                    {
                        Enabled = beatSnapshot.Enabled,
                        Bpm = beatSnapshot.Bpm,
                        AutoMotionSwitch = beatSnapshot.AutoMotionSwitch,
                        AutoThreshold = beatSnapshot.AutoThreshold,
                        LowThreshold = beatSnapshot.LowThreshold,
                        HighThreshold = beatSnapshot.HighThreshold,
                        LowIntensity = beatSnapshot.LowIntensity,
                        MidIntensity = beatSnapshot.MidIntensity,
                        HighIntensity = beatSnapshot.HighIntensity,
                        SmoothTime = beatSnapshot.SmoothTime,
                        StrongMotionBeats = beatSnapshot.StrongMotionBeats,
                        WeakMotionBeats = beatSnapshot.WeakMotionBeats,
                        LowPassHz = beatSnapshot.LowPassHz,
                        VerboseLog = beatSnapshot.VerboseLog
                    };
                    bool beatChanged = false;

                    float beatSaveW = 52f;
                    float beatSaveGap = 4f;
                    float beatSaveVideoX = beatSyncX + beatSyncW - beatSaveW - 8f;
                    float beatSaveFolderX = beatSaveVideoX - beatSaveW - beatSaveGap;

                    float contentX = beatSyncX + 8f;
                    float contentW = beatSyncW - 16f;

                    float cursorX = contentX;
                    bool nextBeatEnabled = HelpToggle(
                        new Rect(cursorX, row1Y, 72f, rowH),
                        nextBeat.Enabled,
                        "Enabled",
                        "ビート同期を有効化");
                    if (nextBeatEnabled != nextBeat.Enabled)
                    {
                        nextBeat.Enabled = nextBeatEnabled;
                        beatChanged = true;
                    }
                    cursorX += 76f;

                    bool nextBeatAutoMotion = HelpToggle(
                        new Rect(cursorX, row1Y, 98f, rowH),
                        nextBeat.AutoMotionSwitch,
                        "AutoMotion",
                        "強弱に応じてモーション段階を自動切替");
                    if (nextBeatAutoMotion != nextBeat.AutoMotionSwitch)
                    {
                        nextBeat.AutoMotionSwitch = nextBeatAutoMotion;
                        beatChanged = true;
                    }
                    cursorX += 102f;

                    bool nextAutoThreshold = HelpToggle(
                        new Rect(cursorX, row1Y, 104f, rowH),
                        nextBeat.AutoThreshold,
                        "AutoThreshold",
                        "閾値を自動推定");
                    if (nextAutoThreshold != nextBeat.AutoThreshold)
                    {
                        nextBeat.AutoThreshold = nextAutoThreshold;
                        beatChanged = true;
                    }
                    cursorX += 108f;

                    bool nextVerbose = HelpToggle(
                        new Rect(cursorX, row1Y, 96f, rowH),
                        nextBeat.VerboseLog,
                        "VerboseLog",
                        "BeatSync詳細ログ出力");
                    if (nextVerbose != nextBeat.VerboseLog)
                    {
                        nextBeat.VerboseLog = nextVerbose;
                        beatChanged = true;
                    }
                    float groupsTopY = row1Y + 22f;
                    float groupGapX = 6f;
                    float groupGapY = 4f;
                    float groupH = 46f;
                    float groupLeftW = Mathf.Max(220f, (contentW - groupGapX) * 0.62f);
                    groupLeftW = Mathf.Min(groupLeftW, contentW - groupGapX - 140f);
                    float groupRightW = contentW - groupGapX - groupLeftW;
                    float groupLeftX = contentX;
                    float groupRightX = groupLeftX + groupLeftW + groupGapX;
                    float bottomGroupsY = groupsTopY + groupH + groupGapY;

                    GUI.Box(new Rect(groupLeftX, groupsTopY, groupLeftW, groupH), "Analysis");
                    GUI.Box(new Rect(groupRightX, groupsTopY, groupRightW, groupH), "Threshold");
                    GUI.Box(new Rect(groupLeftX, bottomGroupsY, groupLeftW, groupH), "Intensity");
                    GUI.Box(new Rect(groupRightX, bottomGroupsY, groupRightW, groupH), "Motion Switch");

                    // Analysis group (BPM / LowPassHz / SmoothTime)
                    float analysisGap = 6f;
                    float analysisFieldW = (groupLeftW - 12f - analysisGap * 2f) / 3f;
                    float analysisLabelW = Mathf.Clamp(analysisFieldW * 0.58f, 42f, 72f);
                    float analysisInputW = Mathf.Max(24f, analysisFieldW - analysisLabelW - 2f);
                    float analysisY = groupsTopY + 14f;
                    float analysisSliderY = analysisY + 20f;

                    float a0x = groupLeftX + 6f + (analysisFieldW + analysisGap) * 0f;
                    float a1x = groupLeftX + 6f + (analysisFieldW + analysisGap) * 1f;
                    float a2x = groupLeftX + 6f + (analysisFieldW + analysisGap) * 2f;

                    GUI.Label(new Rect(a0x, analysisY, analysisLabelW, rowH), "BPM");
                    if (DrawRoomNumericInput("BeatSyncBpmInputGroup", new Rect(a0x + analysisLabelW, analysisY, analysisInputW, 18f), ref _beatSyncBpmInput, nextBeat.Bpm, out float typedBpm))
                    {
                        int clamped = Mathf.Clamp(Mathf.RoundToInt(typedBpm), 1, 999);
                        if (clamped != nextBeat.Bpm) { nextBeat.Bpm = clamped; beatChanged = true; }
                    }
                    float bpmSlider = HelpSlider(
                        new Rect(a0x, analysisSliderY, analysisFieldW, 12f),
                        nextBeat.Bpm,
                        1f,
                        999f,
                        "推定BPM");
                    int bpmRounded = Mathf.Clamp(Mathf.RoundToInt(bpmSlider), 1, 999);
                    if (bpmRounded != nextBeat.Bpm) { nextBeat.Bpm = bpmRounded; _beatSyncBpmInput = FormatRoomNumeric(bpmRounded); beatChanged = true; }

                    GUI.Label(new Rect(a1x, analysisY, analysisLabelW, rowH), "LowPassHz");
                    if (DrawRoomNumericInput("BeatSyncLowPassInputGroup", new Rect(a1x + analysisLabelW, analysisY, analysisInputW, 18f), ref _beatSyncLowPassInput, nextBeat.LowPassHz, out float typedLowPass))
                    {
                        float clamped = Mathf.Clamp(typedLowPass, 50f, 500f);
                        if (Mathf.Abs(clamped - nextBeat.LowPassHz) > 0.0001f) { nextBeat.LowPassHz = clamped; beatChanged = true; }
                    }
                    float lowPassSlider = HelpSlider(
                        new Rect(a1x, analysisSliderY, analysisFieldW, 12f),
                        nextBeat.LowPassHz,
                        50f,
                        500f,
                        "解析ローパス周波数");
                    if (Mathf.Abs(lowPassSlider - nextBeat.LowPassHz) > 0.0001f) { nextBeat.LowPassHz = lowPassSlider; _beatSyncLowPassInput = FormatRoomNumeric(lowPassSlider); beatChanged = true; }

                    GUI.Label(new Rect(a2x, analysisY, analysisLabelW, rowH), "SmoothTime");
                    if (DrawRoomNumericInput("BeatSyncSmoothTimeInputGroup", new Rect(a2x + analysisLabelW, analysisY, analysisInputW, 18f), ref _beatSyncSmoothTimeInput, nextBeat.SmoothTime, out float typedSmooth))
                    {
                        float clamped = Mathf.Clamp(typedSmooth, 0f, 2f);
                        if (Mathf.Abs(clamped - nextBeat.SmoothTime) > 0.0001f) { nextBeat.SmoothTime = clamped; beatChanged = true; }
                    }
                    float smoothSlider = HelpSlider(
                        new Rect(a2x, analysisSliderY, analysisFieldW, 12f),
                        nextBeat.SmoothTime,
                        0f,
                        2f,
                        "強度追従の平滑時間");
                    if (Mathf.Abs(smoothSlider - nextBeat.SmoothTime) > 0.0001f) { nextBeat.SmoothTime = smoothSlider; _beatSyncSmoothTimeInput = FormatRoomNumeric(smoothSlider); beatChanged = true; }

                    // Threshold group (Low/High)
                    float thresholdGap = 6f;
                    float thresholdFieldW = (groupRightW - 12f - thresholdGap) / 2f;
                    float thresholdLabelW = Mathf.Clamp(thresholdFieldW * 0.60f, 44f, 78f);
                    float thresholdInputW = Mathf.Max(22f, thresholdFieldW - thresholdLabelW - 2f);
                    float thresholdY = groupsTopY + 14f;
                    float thresholdSliderY = thresholdY + 20f;

                    float t0x = groupRightX + 6f;
                    float t1x = t0x + thresholdFieldW + thresholdGap;

                    GUI.Label(new Rect(t0x, thresholdY, thresholdLabelW, rowH), "LowThreshold");
                    if (DrawRoomNumericInput("BeatSyncLowThresholdInputGroup", new Rect(t0x + thresholdLabelW, thresholdY, thresholdInputW, 18f), ref _beatSyncLowThresholdInput, nextBeat.LowThreshold, out float typedLowTh))
                    {
                        float clamped = Mathf.Clamp01(typedLowTh);
                        if (Mathf.Abs(clamped - nextBeat.LowThreshold) > 0.0001f)
                        {
                            nextBeat.LowThreshold = clamped;
                            if (nextBeat.HighThreshold <= nextBeat.LowThreshold + 0.01f)
                                nextBeat.HighThreshold = Mathf.Min(1f, nextBeat.LowThreshold + 0.01f);
                            beatChanged = true;
                        }
                    }
                    float lowThSlider = HelpSlider(
                        new Rect(t0x, thresholdSliderY, thresholdFieldW, 12f),
                        nextBeat.LowThreshold,
                        0f,
                        1f,
                        "弱/中の下限しきい値");
                    if (Mathf.Abs(lowThSlider - nextBeat.LowThreshold) > 0.0001f)
                    {
                        nextBeat.LowThreshold = lowThSlider;
                        if (nextBeat.HighThreshold <= nextBeat.LowThreshold + 0.01f)
                            nextBeat.HighThreshold = Mathf.Min(1f, nextBeat.LowThreshold + 0.01f);
                        _beatSyncLowThresholdInput = FormatRoomNumeric(nextBeat.LowThreshold);
                        _beatSyncHighThresholdInput = FormatRoomNumeric(nextBeat.HighThreshold);
                        beatChanged = true;
                    }

                    GUI.Label(new Rect(t1x, thresholdY, thresholdLabelW, rowH), "HighThreshold");
                    if (DrawRoomNumericInput("BeatSyncHighThresholdInputGroup", new Rect(t1x + thresholdLabelW, thresholdY, thresholdInputW, 18f), ref _beatSyncHighThresholdInput, nextBeat.HighThreshold, out float typedHighTh))
                    {
                        float clamped = Mathf.Clamp(typedHighTh, nextBeat.LowThreshold + 0.01f, 1f);
                        if (Mathf.Abs(clamped - nextBeat.HighThreshold) > 0.0001f) { nextBeat.HighThreshold = clamped; beatChanged = true; }
                    }
                    float highThSlider = HelpSlider(
                        new Rect(t1x, thresholdSliderY, thresholdFieldW, 12f),
                        nextBeat.HighThreshold,
                        nextBeat.LowThreshold + 0.01f,
                        1f,
                        "中/強の上限しきい値");
                    if (Mathf.Abs(highThSlider - nextBeat.HighThreshold) > 0.0001f) { nextBeat.HighThreshold = highThSlider; _beatSyncHighThresholdInput = FormatRoomNumeric(highThSlider); beatChanged = true; }

                    // Intensity group (Low/Mid/High)
                    float intensityGap = 6f;
                    float intensityFieldW = (groupLeftW - 12f - intensityGap * 2f) / 3f;
                    float intensityLabelW = Mathf.Clamp(intensityFieldW * 0.60f, 44f, 76f);
                    float intensityInputW = Mathf.Max(24f, intensityFieldW - intensityLabelW - 2f);
                    float intensityY = bottomGroupsY + 14f;
                    float intensitySliderY = intensityY + 20f;

                    float i0x = groupLeftX + 6f + (intensityFieldW + intensityGap) * 0f;
                    float i1x = groupLeftX + 6f + (intensityFieldW + intensityGap) * 1f;
                    float i2x = groupLeftX + 6f + (intensityFieldW + intensityGap) * 2f;

                    GUI.Label(new Rect(i0x, intensityY, intensityLabelW, rowH), "LowIntensity");
                    if (DrawRoomNumericInput("BeatSyncLowIntensityInputGroup", new Rect(i0x + intensityLabelW, intensityY, intensityInputW, 18f), ref _beatSyncLowIntensityInput, nextBeat.LowIntensity, out float typedLowInt))
                    {
                        float clamped = Mathf.Clamp01(typedLowInt);
                        if (Mathf.Abs(clamped - nextBeat.LowIntensity) > 0.0001f) { nextBeat.LowIntensity = clamped; beatChanged = true; }
                    }
                    float lowIntSlider = HelpSlider(
                        new Rect(i0x, intensitySliderY, intensityFieldW, 12f),
                        nextBeat.LowIntensity,
                        0f,
                        1f,
                        "弱拍時の強度");
                    if (Mathf.Abs(lowIntSlider - nextBeat.LowIntensity) > 0.0001f) { nextBeat.LowIntensity = lowIntSlider; _beatSyncLowIntensityInput = FormatRoomNumeric(lowIntSlider); beatChanged = true; }

                    GUI.Label(new Rect(i1x, intensityY, intensityLabelW, rowH), "MidIntensity");
                    if (DrawRoomNumericInput("BeatSyncMidIntensityInputGroup", new Rect(i1x + intensityLabelW, intensityY, intensityInputW, 18f), ref _beatSyncMidIntensityInput, nextBeat.MidIntensity, out float typedMidInt))
                    {
                        float clamped = Mathf.Clamp01(typedMidInt);
                        if (Mathf.Abs(clamped - nextBeat.MidIntensity) > 0.0001f) { nextBeat.MidIntensity = clamped; beatChanged = true; }
                    }
                    float midIntSlider = HelpSlider(
                        new Rect(i1x, intensitySliderY, intensityFieldW, 12f),
                        nextBeat.MidIntensity,
                        0f,
                        1f,
                        "中拍時の強度");
                    if (Mathf.Abs(midIntSlider - nextBeat.MidIntensity) > 0.0001f) { nextBeat.MidIntensity = midIntSlider; _beatSyncMidIntensityInput = FormatRoomNumeric(midIntSlider); beatChanged = true; }

                    GUI.Label(new Rect(i2x, intensityY, intensityLabelW, rowH), "HighIntensity");
                    if (DrawRoomNumericInput("BeatSyncHighIntensityInputGroup", new Rect(i2x + intensityLabelW, intensityY, intensityInputW, 18f), ref _beatSyncHighIntensityInput, nextBeat.HighIntensity, out float typedHighInt))
                    {
                        float clamped = Mathf.Clamp01(typedHighInt);
                        if (Mathf.Abs(clamped - nextBeat.HighIntensity) > 0.0001f) { nextBeat.HighIntensity = clamped; beatChanged = true; }
                    }
                    float highIntSlider = HelpSlider(
                        new Rect(i2x, intensitySliderY, intensityFieldW, 12f),
                        nextBeat.HighIntensity,
                        0f,
                        1f,
                        "強拍時の強度");
                    if (Mathf.Abs(highIntSlider - nextBeat.HighIntensity) > 0.0001f) { nextBeat.HighIntensity = highIntSlider; _beatSyncHighIntensityInput = FormatRoomNumeric(highIntSlider); beatChanged = true; }

                    // Motion Switch group (Strong/Weak beats)
                    float motionGap = 6f;
                    float motionFieldW = (groupRightW - 12f - motionGap) / 2f;
                    float motionLabelW = Mathf.Clamp(motionFieldW * 0.64f, 48f, 84f);
                    float motionInputW = Mathf.Max(22f, motionFieldW - motionLabelW - 2f);
                    float motionY = bottomGroupsY + 14f;
                    float motionSliderY = motionY + 20f;

                    float m0x = groupRightX + 6f;
                    float m1x = m0x + motionFieldW + motionGap;

                    GUI.Label(new Rect(m0x, motionY, motionLabelW, rowH), "StrongBeats");
                    if (DrawRoomNumericInput("BeatSyncStrongBeatsInputGroup", new Rect(m0x + motionLabelW, motionY, motionInputW, 18f), ref _beatSyncStrongBeatsInput, nextBeat.StrongMotionBeats, out float typedStrong))
                    {
                        float clamped = Mathf.Clamp(typedStrong, 0.5f, 64f);
                        if (Mathf.Abs(clamped - nextBeat.StrongMotionBeats) > 0.0001f) { nextBeat.StrongMotionBeats = clamped; beatChanged = true; }
                    }
                    float strongSlider = HelpSlider(
                        new Rect(m0x, motionSliderY, motionFieldW, 12f),
                        nextBeat.StrongMotionBeats,
                        0.5f,
                        64f,
                        "強モーション切替の拍数");
                    if (Mathf.Abs(strongSlider - nextBeat.StrongMotionBeats) > 0.0001f) { nextBeat.StrongMotionBeats = strongSlider; _beatSyncStrongBeatsInput = FormatRoomNumeric(strongSlider); beatChanged = true; }

                    GUI.Label(new Rect(m1x, motionY, motionLabelW, rowH), "WeakBeats");
                    if (DrawRoomNumericInput("BeatSyncWeakBeatsInputGroup", new Rect(m1x + motionLabelW, motionY, motionInputW, 18f), ref _beatSyncWeakBeatsInput, nextBeat.WeakMotionBeats, out float typedWeak))
                    {
                        float clamped = Mathf.Clamp(typedWeak, 0.5f, 64f);
                        if (Mathf.Abs(clamped - nextBeat.WeakMotionBeats) > 0.0001f) { nextBeat.WeakMotionBeats = clamped; beatChanged = true; }
                    }
                    float weakSlider = HelpSlider(
                        new Rect(m1x, motionSliderY, motionFieldW, 12f),
                        nextBeat.WeakMotionBeats,
                        0.5f,
                        64f,
                        "弱モーション切替の拍数");
                    if (Mathf.Abs(weakSlider - nextBeat.WeakMotionBeats) > 0.0001f) { nextBeat.WeakMotionBeats = weakSlider; _beatSyncWeakBeatsInput = FormatRoomNumeric(weakSlider); beatChanged = true; }

                    if (beatChanged && TryApplyBeatSyncSnapshot(nextBeat, "bar-ui"))
                        InvalidateBeatSyncSnapshotBarCache();

                    if (HelpButton(new Rect(beatSaveFolderX, row1Y, beatSaveW, 18f), "SaveF", "BeatSync設定をフォルダ設定に保存"))
                        SaveFolderBeatSyncProfileFromBar();
                    if (HelpButton(new Rect(beatSaveVideoX, row1Y, beatSaveW, 18f), "SaveV", "BeatSync設定を動画個別設定に保存"))
                        SaveVideoBeatSyncProfileFromBar();
                }
                else
                {
                    GUI.Label(
                        new Rect(beatSyncX + 8f, row1Y, beatSyncW - 16f, rowH),
                        "(plugin not loaded)");
                }

                float overlayTopY = integrationTopY + integrationSectionH + controlsGapY;
                DrawImageSlideshowSection(beatSyncX, overlayTopY, beatSyncW, slideshowSectionH);
                overlayTopY += slideshowSectionH + controlsGapY;
                DrawSubCameraOverlaySection(beatSyncX, overlayTopY, beatSyncW, overlaySectionH);
            }

            float x = centerX;
            float panelToggleW = 22f;
            float helpToggleW = 62f;
            float unifiedUiToggleW = 84f;
            float impactBoostToggleW = 102f;
            float milkSprayToggleW = 72f;
            float impactMoanModeToggleW = 114f;
            float afterimageToggleW = 94f;
#if SHOW_EXPERIMENTAL_VRFX_BAR
            float vrScreenEffectToggleW = 58f;
            float vrScreenEffectUiToggleW = 66f;
#endif
            float dollModeToggleW = 84f;
            float poseToggleW = 94f;
            float scenarioAutoToggleW = 0f;
            float scenarioManualButtonW = 78f;
            float toggleGap = 4f;
            float panelToggleX = x - panelToggleW - pad;
#if SHOW_EXPERIMENTAL_VRFX_BAR
            float toggleGroupW = helpToggleW + unifiedUiToggleW + impactBoostToggleW + milkSprayToggleW + impactMoanModeToggleW + afterimageToggleW + vrScreenEffectToggleW + vrScreenEffectUiToggleW + dollModeToggleW + poseToggleW + scenarioAutoToggleW + scenarioManualButtonW + toggleGap * 11f;
#else
            float toggleGroupW = helpToggleW + unifiedUiToggleW + impactBoostToggleW + milkSprayToggleW + impactMoanModeToggleW + afterimageToggleW + dollModeToggleW + poseToggleW + scenarioAutoToggleW + scenarioManualButtonW + toggleGap * 9f;
#endif
            float toggleGroupX = 0;
            float helpToggleX = toggleGroupX;
            float unifiedUiToggleX = helpToggleX + helpToggleW + toggleGap;
            float impactBoostToggleX = unifiedUiToggleX + unifiedUiToggleW + toggleGap;
            float milkSprayToggleX = impactBoostToggleX + impactBoostToggleW + toggleGap;
            float impactMoanModeToggleX = milkSprayToggleX + milkSprayToggleW + toggleGap;
            float afterimageToggleX = impactMoanModeToggleX + impactMoanModeToggleW + toggleGap;
#if SHOW_EXPERIMENTAL_VRFX_BAR
            float vrScreenEffectToggleX = afterimageToggleX + afterimageToggleW + toggleGap;
            float vrScreenEffectUiToggleX = vrScreenEffectToggleX + vrScreenEffectToggleW + toggleGap;
            float dollModeToggleX = vrScreenEffectUiToggleX + vrScreenEffectUiToggleW + toggleGap;
#else
            float dollModeToggleX = afterimageToggleX + afterimageToggleW + toggleGap;
#endif
            float poseToggleX = dollModeToggleX + dollModeToggleW + toggleGap;
            float scenarioAutoToggleX = poseToggleX + poseToggleW + toggleGap;
            float scenarioManualButtonX = scenarioAutoToggleX + scenarioAutoToggleW + toggleGap;
            float helpToggleH = 18f;
            float transportListY = y + PlaybackTransportLayout.ListOffset(barRect.width);
            float helpToggleY = transportListY + 26f;
            float toggleViewportY = helpToggleY;
            float toggleRowHeight = PlaybackTransportLayout.ToggleRowHeight(barRect.width);
            _transportToggleScroll = GUI.BeginScrollView(new Rect(barRect.x + 6, toggleViewportY, barRect.width - 12, toggleRowHeight), _transportToggleScroll, new Rect(0, 0, toggleGroupW, 20), GUI.skin.horizontalScrollbar, GUIStyle.none);
            helpToggleY = 0;
            bool currentHelpPopup = _settings.EnableUiHelpPopup;
            bool nextHelpPopup = HelpToggle(
                new Rect(helpToggleX, helpToggleY, helpToggleW, helpToggleH),
                currentHelpPopup,
                "説明",
                "説明ポップアップ表示");
            if (nextHelpPopup != currentHelpPopup)
                ApplyPlaybackBarHelpPopupToggle(nextHelpPopup, persist: true);

            RefreshExternalUiSnapshotBarCacheIfNeeded();

            bool unifiedUiAvailable = _unifiedUiSnapshotBarAvailable;
            bool unifiedUiVisibleNow = _unifiedUiSnapshotBarVisible;
            bool prevGuiEnabled = GUI.enabled;
            GUI.enabled = prevGuiEnabled && unifiedUiAvailable;
            bool nextUnifiedUiVisible = HelpToggle(
                new Rect(unifiedUiToggleX, helpToggleY, unifiedUiToggleW, helpToggleH),
                unifiedUiVisibleNow,
                "UnifiedUI",
                unifiedUiAvailable
                    ? "MainGameUnifiedUi の表示/非表示"
                    : "MainGameUnifiedUi が未ロード");
            GUI.enabled = prevGuiEnabled;
            if (unifiedUiAvailable && nextUnifiedUiVisible != unifiedUiVisibleNow)
            {
                if (TryApplyUnifiedUiVisible(nextUnifiedUiVisible, "bar-ui-toggle"))
                {
                    _unifiedUiSnapshotBarVisible = nextUnifiedUiVisible;
                }
                else
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }

            bool afterimageAvailable = _afterimageSnapshotBarAvailable;
            if (_impactBoostEnabled != null)
            {
                bool nextImpact = HelpToggle(
                    new Rect(impactBoostToggleX, helpToggleY, impactBoostToggleW, helpToggleH),
                    _impactBoostEnabled.Value, "ImpactBoost",
                    "ON: 通常/VRの対応側だけ有効。OFF: 両方無効");
                if (nextImpact != _impactBoostEnabled.Value)
                {
                    _impactBoostEnabled.Value = nextImpact;
                    UpdateImpactBoost();
                }
            }
            var milkSprayEnabled = GetMilkSprayEnabledEntry();
            GUI.enabled = prevGuiEnabled && milkSprayEnabled != null;
            bool nextMilkSpray = HelpToggle(
                new Rect(milkSprayToggleX, helpToggleY, milkSprayToggleW, helpToggleH),
                milkSprayEnabled != null && milkSprayEnabled.Value, "母乳",
                milkSprayEnabled != null ? "絶頂準備中のピストンごとの母乳演出。Configuration Manager と同期" : "MainGameMilkSpray が未ロード");
            if (milkSprayEnabled != null && nextMilkSpray != milkSprayEnabled.Value)
                milkSprayEnabled.Value = nextMilkSpray;
            GUI.enabled = prevGuiEnabled;
            if (_impactMoanModeEnabled != null)
            {
                bool nextMode = HelpToggle(
                    new Rect(impactMoanModeToggleX, helpToggleY, impactMoanModeToggleW, helpToggleH),
                    _impactMoanModeEnabled.Value, "喘ぎ声モード",
                    "ImpactBoost ON時に使用。ON: 選択した音源から挿入、OFF: 音量ブースト。セリフがなくても発火");
                if (nextMode != _impactMoanModeEnabled.Value)
                {
                    _impactMoanModeEnabled.Value = nextMode;
                    UpdateImpactBoost();
                }
            }
            bool afterimageEnabledNow = _afterimageSnapshotBarEnabled;
            GUI.enabled = prevGuiEnabled && afterimageAvailable;
            bool nextAfterimageEnabled = HelpToggle(
                new Rect(afterimageToggleX, helpToggleY, afterimageToggleW, helpToggleH),
                afterimageEnabledNow,
                "AfterImage",
                afterimageAvailable
                    ? "SimpleAfterimage の有効/無効"
                    : "SimpleAfterimage が未ロード");
            GUI.enabled = prevGuiEnabled;
            if (afterimageAvailable && nextAfterimageEnabled != afterimageEnabledNow)
            {
                if (TryApplyAfterimageEnabled(nextAfterimageEnabled, "bar-ui-toggle"))
                {
                    _afterimageSnapshotBarEnabled = nextAfterimageEnabled;
                }
                else
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }

#if SHOW_EXPERIMENTAL_VRFX_BAR
            bool vrScreenEffectAvailable = _vrScreenEffectSnapshotBarAvailable;
            bool vrScreenEffectEnabledNow = _vrScreenEffectSnapshotBarEnabled;
            GUI.enabled = prevGuiEnabled && vrScreenEffectAvailable;
            bool nextVrScreenEffectEnabled = HelpToggle(
                new Rect(vrScreenEffectToggleX, helpToggleY, vrScreenEffectToggleW, helpToggleH),
                vrScreenEffectEnabledNow,
                "VRFx",
                vrScreenEffectAvailable
                    ? "MainGameVrScreenEffectProbe の有効/無効"
                    : "MainGameVrScreenEffectProbe が未ロード");
            GUI.enabled = prevGuiEnabled;
            if (vrScreenEffectAvailable && nextVrScreenEffectEnabled != vrScreenEffectEnabledNow)
            {
                if (TryApplyVrScreenEffectEnabled(nextVrScreenEffectEnabled, "bar-ui-toggle"))
                {
                    _vrScreenEffectSnapshotBarEnabled = nextVrScreenEffectEnabled;
                }
                else
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }

            bool vrScreenEffectUiAvailable = _vrScreenEffectUiSnapshotBarAvailable;
            bool vrScreenEffectUiVisibleNow = _vrScreenEffectUiSnapshotBarVisible;
            GUI.enabled = prevGuiEnabled && vrScreenEffectUiAvailable;
            bool nextVrScreenEffectUiVisible = HelpToggle(
                new Rect(vrScreenEffectUiToggleX, helpToggleY, vrScreenEffectUiToggleW, helpToggleH),
                vrScreenEffectUiVisibleNow,
                "VRFxUI",
                vrScreenEffectUiAvailable
                    ? "MainGameVrScreenEffectProbe UIの表示/非表示"
                    : "MainGameVrScreenEffectProbe が未ロード");
            GUI.enabled = prevGuiEnabled;
            if (vrScreenEffectUiAvailable && nextVrScreenEffectUiVisible != vrScreenEffectUiVisibleNow)
            {
                if (TryApplyVrScreenEffectUiVisible(nextVrScreenEffectUiVisible, "bar-ui-toggle"))
                {
                    _vrScreenEffectUiSnapshotBarVisible = nextVrScreenEffectUiVisible;
                }
                else
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }
#endif

            bool dollModeAvailable = _dollModeSnapshotBarAvailable;
            bool dollModeEnabledNow = _dollModeSnapshotBarEnabled;
            GUI.enabled = prevGuiEnabled && dollModeAvailable;
            bool nextDollModeEnabled = HelpToggle(
                new Rect(dollModeToggleX, helpToggleY, dollModeToggleW, helpToggleH),
                dollModeEnabledNow,
                "人形",
                dollModeAvailable
                    ? "MainGameDollMode の有効/無効"
                    : "MainGameDollMode が未ロード");
            GUI.enabled = prevGuiEnabled;
            if (dollModeAvailable && nextDollModeEnabled != dollModeEnabledNow)
            {
                if (TryApplyDollModeEnabled(nextDollModeEnabled, "bar-ui-toggle"))
                {
                    _dollModeSnapshotBarEnabled = nextDollModeEnabled;
                }
                else
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }

            bool poseAvailable = _poseSnapshotBarAvailable;
            bool poseEnabledNow = _poseSnapshotBarEnabled;
            GUI.enabled = prevGuiEnabled && poseAvailable;
            bool nextPoseEnabled = HelpToggle(
                new Rect(poseToggleX, helpToggleY, poseToggleW, helpToggleH),
                poseEnabledNow,
                "体位変更",
                poseAvailable
                    ? "VoiceFaceEventBridge 体位変更の有効/無効"
                    : "VoiceFaceEventBridge が未ロード");
            GUI.enabled = prevGuiEnabled;
            if (poseAvailable && nextPoseEnabled != poseEnabledNow)
            {
                if (TryApplyVfebPoseChangeEnabled(nextPoseEnabled, "bar-ui-toggle"))
                {
                    _poseSnapshotBarEnabled = nextPoseEnabled;
                }
                else
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }

            bool scenarioTextAvailable = _scenarioTextSnapshotBarAvailable;
            GUI.enabled = prevGuiEnabled && scenarioTextAvailable;
            bool scenarioManualClicked = HelpButton(
                new Rect(scenarioManualButtonX, helpToggleY, scenarioManualButtonW, helpToggleH),
                "状況送信",
                scenarioTextAvailable
                    ? "VoiceFaceEventBridge 状況文を今すぐ手動送信"
                    : "VoiceFaceEventBridge 状況文送信APIが未ロード");
            GUI.enabled = prevGuiEnabled;
            if (scenarioManualClicked)
            {
                if (!TrySendVfebScenarioTextManualNow("bar-manual-button"))
                {
                    InvalidateExternalUiSnapshotBarCache();
                }
            }

            GUI.EndScrollView();
            string panelToggleLabel = _playbackRoomControlsExpanded ? "▼" : "▲";
            if (HelpButtonContent(
                new Rect(panelToggleX, y, panelToggleW, buttonH),
                new GUIContent(panelToggleLabel, "上段パネルの展開/折りたたみ")))
            {
                _playbackRoomControlsExpanded = !_playbackRoomControlsExpanded;
            }

            Rect folderDropdownButtonRect = Rect.zero;
            Rect folderDropdownListRect = Rect.zero;
            Rect videoDropdownButtonRect = Rect.zero;
            Rect videoDropdownListRect = Rect.zero;
            float transportActionsEndX = x;
            float transportActionsY = y;
            DrawVmdBarMode(ref x, y, buttonH);
            if (_vmdBarMode) {
                DrawVmdTransport(x, y, buttonH, barRect, transportListY, out folderDropdownListRect, out videoDropdownListRect);
                transportActionsEndX = x + 300f + (barRect.width >= 650f ? 130f : 0f);
                currentSec = (float)_vmdBarState[4]; totalSec = (float)_vmdBarState[5];
            } else {
            GUI.enabled = prevGuiEnabled && !ExternalMediaBlocksPlayback;
            if (HelpButton(new Rect(x, y, buttonW, buttonH), "Play", ExternalMediaBlocksPlayback ? "VMD音源の再生中は使用できません" : "動画再生"))
            {
                PlayFromBar(player);
            }
            GUI.enabled = prevGuiEnabled;
            x += buttonW + pad;

            GUI.enabled = player != null;
            if (HelpButton(new Rect(x, y, buttonW, buttonH), "Pause", "動画一時停止"))
            {
                player.Pause();
                LogInfo("video pause (bar)");
                LogAudioDiagnosticsSnapshot("bar-pause", includeStoppedSources: true);
            }
            x += buttonW + pad;

            if (HelpButton(new Rect(x, y, buttonW, buttonH), "Stop", "動画停止（先頭へ戻す）"))
            {
                ClearDeferredPlayFromBar();
                player.Stop();
                _playbackSeekNormalized = 0f;
                LogInfo("video stop (bar)");
                LogAudioDiagnosticsSnapshot("bar-stop", includeStoppedSources: true);
            }
            GUI.enabled = true;
            x += buttonW + pad;

            // _reverbDropdownButtonRect / _reverbDropdownListRect はフィールド変数（前フレームRepaintの値を保持）

            if (_settings.FolderPlayEnabled)
            {
                if (HelpButton(new Rect(x, y, buttonW, buttonH), "|<", "前の動画へ"))
                {
                    int prev = FolderIndex - 1;
                    if (prev < 0 && _settings.FolderPlayLoop) prev = FolderFiles.Length - 1;
                    if (prev >= 0) PlayFolderEntry(prev);
                }
                x += buttonW + pad;

                if (HelpButton(new Rect(x, y, buttonW, buttonH), ">|", "次の動画へ"))
                {
                    int next = FolderIndex + 1;
                    if (next >= FolderFiles.Length && _settings.FolderPlayLoop) next = 0;
                    if (next < FolderFiles.Length) PlayFolderEntry(next);
                }
                x += buttonW + pad;

                float transportY = y;
                if (PlaybackTransportLayout.WrapTransport(barRect.width)) { x = barRect.x + 6; y += 26; }
                string loopLabel = _settings.FolderPlaySingleLoop ? "1Loop ON" : "1Loop OFF";
                if (HelpButton(new Rect(x, y, loopButtonW, buttonH), loopLabel, "1曲ループのON/OFF"))
                {
                    bool nextSingle = !_settings.FolderPlaySingleLoop;
                    _settings.FolderPlaySingleLoop = nextSingle;

                    // ConfigManager経由の再構築は不要なので同期モードで値だけ合わせる。
                    bool prevSync = _syncingConfig;
                    _syncingConfig = true;
                    try
                    {
                        if (_cfgFolderPlaySingleLoop != null)
                            _cfgFolderPlaySingleLoop.Value = nextSingle;
                    }
                    finally
                    {
                        _syncingConfig = prevSync;
                        _configDirty = false;
                    }

                    _settingsRepository.SaveCurrent(_settings);
                    LogInfo($"folder play: single loop {(nextSingle ? "on" : "off")} (bar)");
                }
                x += loopButtonW + pad;

                string sortLabel = GetSortModeDisplayLabel(_settings.FolderPlaySortMode);
                if (HelpButton(new Rect(x, y, sortButtonW, buttonH), sortLabel, "並び順を切替 (Name→Date→Random)"))
                {
                    CycleFolderSortModeFromBar();
                }
                x += sortButtonW + pad;

                string tileButtonLabel = $"Tiles:{_settings.CubeFaceTileCount}";
                if (HelpButton(new Rect(x, y, tileButtonW, buttonH), tileButtonLabel, "1面あたりの動画パネル枚数を切替"))
                {
                    int nextTiles = GetNextCubeFaceTileCount(_settings.CubeFaceTileCount);
                    ApplyCubeFaceTileCountFromBar(nextTiles);
                }
                x += tileButtonW + pad;

                if (HelpButton(new Rect(x, y, addFolderButtonW, buttonH), "フォルダ登録", "再生フォルダを追加"))
                {
                    string currentFolder = NormalizeFolderPathForList(_settings.FolderPlayPath);
                    if (!TryOpenFolderDialog(currentFolder, out string selectedPath, out string error))
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                            LogWarn($"folder add failed error={error}");
                    }
                    else if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        AddFolderPlayPathFromBar(selectedPath);
                    }
                }
                x += addFolderButtonW + pad;
                transportActionsEndX = x;
                transportActionsY = y;

                y = transportY;
                x = barRect.x + 6;
                folderDropdownW = PlaybackTransportLayout.ChoiceWidth(barRect.width) * 0.5f;
                videoDropdownW = PlaybackTransportLayout.ChoiceWidth(barRect.width) * 0.75f;
                string folderDropLabel = $"Folder:{GetFolderDisplayNameForBar(_settings.FolderPlayPath)}";
                folderDropdownButtonRect = new Rect(x, transportListY, folderDropdownW, buttonH);
                if (HelpButton(folderDropdownButtonRect, folderDropLabel, "登録済みフォルダを選択"))
                {
                    _folderDropdownOpen = !_folderDropdownOpen;
                    if (_folderDropdownOpen)
                        _videoDropdownOpen = false;
                }
                x += folderDropdownW + pad;

                string currentVideoName = (FolderIndex >= 0 && FolderIndex < FolderFiles.Length)
                    ? GetVideoDisplayNameForBar(FolderFiles[FolderIndex])
                    : "(none)";
                string videoDropLabel = $"Video:{currentVideoName}";
                videoDropdownButtonRect = new Rect(x, transportListY, videoDropdownW, buttonH);
                if (MarqueeButton(videoDropdownButtonRect, videoDropLabel, "current-video"))
                {
                    _videoDropdownOpen = !_videoDropdownOpen;
                    if (_videoDropdownOpen)
                        _folderDropdownOpen = false;
                }
                x += videoDropdownW + pad;
            }
            else
            {
                _folderDropdownOpen = false;
                _videoDropdownOpen = false;

                string tileButtonLabel = $"Tiles:{_settings.CubeFaceTileCount}";
                if (HelpButton(new Rect(x, y, tileButtonW, buttonH), tileButtonLabel, "1面あたりの動画パネル枚数を切替"))
                {
                    int nextTiles = GetNextCubeFaceTileCount(_settings.CubeFaceTileCount);
                    ApplyCubeFaceTileCountFromBar(nextTiles);
                }
                x += tileButtonW + pad;
                transportActionsEndX = x;
            }

            if (_settings.FolderPlayEnabled && _folderDropdownOpen)
            {
                var folderPaths = _settings.FolderPlayPaths ?? new System.Collections.Generic.List<string>();
                const float rowHeight = 20f;
                const float listPad = 3f;
                int visibleCount = Mathf.Clamp(folderPaths.Count, 1, 8);
                float listHeight = visibleCount * rowHeight + listPad * 2f;
                float listY = Mathf.Max(2f, folderDropdownButtonRect.y - listHeight - 2f);
                folderDropdownListRect = new Rect(
                    folderDropdownButtonRect.x,
                    listY,
                    folderDropdownButtonRect.width,
                    listHeight);
                GUI.Box(folderDropdownListRect, GUIContent.none);

                if (folderPaths.Count == 0)
                {
                    GUI.Label(
                        new Rect(folderDropdownListRect.x + 6f, folderDropdownListRect.y + 3f, folderDropdownListRect.width - 12f, rowHeight),
                        "(empty)");
                }
                else
                {
                    var viewport = new Rect(
                        folderDropdownListRect.x + 2f,
                        folderDropdownListRect.y + 2f,
                        folderDropdownListRect.width - 4f,
                        folderDropdownListRect.height - 4f);
                    var viewRect = new Rect(
                        0f,
                        0f,
                        Mathf.Max(24f, viewport.width - 18f),
                        folderPaths.Count * rowHeight);
                    _folderDropdownScroll = GUI.BeginScrollView(viewport, _folderDropdownScroll, viewRect, false, true);
                    for (int i = 0; i < folderPaths.Count; i++)
                    {
                        string path = folderPaths[i];
                        bool selected = string.Equals(
                            NormalizeFolderPathForList(path),
                            NormalizeFolderPathForList(_settings.FolderPlayPath),
                            StringComparison.OrdinalIgnoreCase);
                        string itemLabel = selected
                            ? $"> {GetFolderDisplayNameForBar(path)}"
                            : GetFolderDisplayNameForBar(path);
                        var rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 1f);
                        if (!HelpButton(rowRect, itemLabel, "クリックでフォルダ切替"))
                            continue;

                        SetCurrentFolderPlayPathFromBar(path);
                        _folderDropdownOpen = false;
                    }
                    GUI.EndScrollView();
                }
            }

            if (_settings.FolderPlayEnabled && _videoDropdownOpen)
            {
                const float rowHeight = 20f;
                const float listPad = 3f;
                int totalCount = FolderFiles?.Length ?? 0;
                int visibleCount = Mathf.Clamp(totalCount, 1, 10);
                float listHeight = visibleCount * rowHeight + listPad * 2f;
                float listY = Mathf.Max(2f, videoDropdownButtonRect.y - listHeight - 2f);
                videoDropdownListRect = new Rect(
                    videoDropdownButtonRect.x,
                    listY,
                    videoDropdownButtonRect.width,
                    listHeight);
                GUI.Box(videoDropdownListRect, GUIContent.none);

                if (totalCount == 0)
                {
                    GUI.Label(
                        new Rect(videoDropdownListRect.x + 6f, videoDropdownListRect.y + 3f, videoDropdownListRect.width - 12f, rowHeight),
                        "(empty)");
                }
                else
                {
                    var viewport = new Rect(
                        videoDropdownListRect.x + 2f,
                        videoDropdownListRect.y + 2f,
                        videoDropdownListRect.width - 4f,
                        videoDropdownListRect.height - 4f);
                    var viewRect = new Rect(
                        0f,
                        0f,
                        Mathf.Max(24f, viewport.width - 18f),
                        totalCount * rowHeight);
                    _videoDropdownScroll = GUI.BeginScrollView(viewport, _videoDropdownScroll, viewRect, false, true);
                    for (int i = 0; i < totalCount; i++)
                    {
                        string title = GetVideoDisplayNameForBar(FolderFiles[i]);
                        bool selected = i == FolderIndex;
                        string itemLabel = selected ? $"> {title}" : title;
                        var rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 1f);
                        if (!MarqueeButton(rowRect, itemLabel, "video-row-" + i))
                            continue;

                        PlayFolderEntry(i);
                        _videoDropdownOpen = false;
                    }
                    GUI.EndScrollView();
                }
            }

            if ((_folderDropdownOpen || _videoDropdownOpen || _reverbDropdownOpen) &&
                Event.current != null &&
                Event.current.type == EventType.MouseDown)
            {
                bool insideFolderUi = _folderDropdownOpen &&
                    (folderDropdownButtonRect.Contains(mouseGui) || folderDropdownListRect.Contains(mouseGui));
                bool insideVideoUi = _videoDropdownOpen &&
                    (videoDropdownButtonRect.Contains(mouseGui) || videoDropdownListRect.Contains(mouseGui));
                bool insideReverbUi = _reverbDropdownOpen &&
                    (_reverbDropdownButtonRect.Contains(mouseGui) || _reverbDropdownListRect.Contains(mouseGui));
                if (!insideFolderUi && !insideVideoUi && !insideReverbUi)
                {
                    _folderDropdownOpen = false;
                    _videoDropdownOpen = false;
                    _reverbDropdownOpen = false;
                }
            }

            } // normal transport

            // Shared actions are available in both normal playback and VMD mode.
            if (showHandyButtons && barRect.xMax - transportActionsEndX >= 70f)
            {
                bool handyOn = IsHandySyncEnabled();
                if (HelpButton(new Rect(transportActionsEndX, transportActionsY, 64f, buttonH), handyOn ? "H停止" : "H再生", "HandySyncの動作開始・停止"))
                    SetHandySyncEnabled(!handyOn);
                transportActionsEndX += 68f;
            }

            DrawVrDildoButtons(new Rect(transportActionsEndX, transportActionsY, 58f, buttonH));
            DrawEffectButtons(new Rect(transportActionsEndX + 62f, transportActionsY, 132f, buttonH));


            string folderName = "(none)";
            if (!string.IsNullOrWhiteSpace(_settings.FolderPlayPath))
            {
                folderName = Path.GetFileName(_settings.FolderPlayPath.TrimEnd('\\', '/'));
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = _settings.FolderPlayPath;
            }

            string leftLine1 = $"Folder: {folderName}";
            string leftLine2;
            if (_settings.FolderPlayEnabled && FolderFiles.Length > 0 && FolderIndex >= 0 && FolderIndex < FolderFiles.Length)
            {
                string title = Path.GetFileNameWithoutExtension(FolderFiles[FolderIndex]);
                leftLine2 = $"Track: [{FolderIndex + 1}/{FolderFiles.Length}] {title}";
            }
            else
            {
                leftLine2 = $"Video: {GetCurrentVideoFileNameForPreset()}";
            }
            string leftInfoText = $"{leftLine1} | {leftLine2}";

            float infoShiftX = 90f;
            float infoX = leftX + infoShiftX;
            float infoW = Mathf.Max(0f, leftW - infoShiftX);
            float infoRowH = 18f;
            float infoY = y + 1f;

            if (!_vmdBarMode && infoW > 4f)
            {
                GUI.Label(new Rect(infoX, infoY, infoW, infoRowH), leftInfoText);
            }

            string bpmText = TryGetSavedBpmForCurrentVideo(out int savedBpm)
                ? $"BPM:{savedBpm}"
                : "BPM:-";

            float currentVolume = _vmdBarMode ? (float)_vmdBarState[6] : Mathf.Clamp01(_settings.VideoVolume);
            string timeText = $"{FormatSeconds(currentSec)} / {FormatSeconds(totalSec)}";
            // Left half: folder and track. Right half: time, seek, volume and reverb.
            float rowStart = barRect.x + 6 + PlaybackTransportLayout.ChoiceWidth(barRect.width) * 1.25f + 12;
            float rowEnd = barRect.xMax - 6;
            float available = Mathf.Max(1, rowEnd - rowStart);
            bool compact = available < 500;
            float timeWidth = compact ? 76f : 90f;
            float bpmWidth = Mathf.Max(64f, GUI.skin.label.CalcSize(new GUIContent(bpmText)).x);
            float volumeLabelWidth = compact ? 34f : 58f;
            float volumeWidth = compact ? 38f : 72f;
            float reverbWidth = compact ? 65f : 110f;
            float toggleWidth = 60f;
            float seekWidth = Mathf.Max(12f, available - timeWidth - bpmWidth - volumeLabelWidth - volumeWidth - reverbWidth - toggleWidth - 24f);
            float cx = rowStart;
            var timeRect = new Rect(cx, transportListY, timeWidth, rowH); cx += timeWidth + 2;
            var seekSliderRect = new Rect(cx, transportListY + 4, seekWidth, 12); cx += seekWidth + 4;
            var bpmRect = new Rect(cx, transportListY, bpmWidth, rowH); cx += bpmWidth + 4;
            var volumeTextRect = new Rect(cx, transportListY, volumeLabelWidth, rowH); cx += volumeLabelWidth + 2;
            var volumeSliderRect = new Rect(cx, transportListY + 4, volumeWidth, 12); cx += volumeWidth + 4;
            _reverbDropdownButtonRect = new Rect(cx, transportListY, reverbWidth, rowH); cx += reverbWidth + 4;
            var videoReverbToggleRect = new Rect(cx, transportListY, toggleWidth, rowH);
            GUI.Label(
                timeRect,
                timeText);
            GUI.Label(bpmRect, bpmText);
            GUI.Label(
                volumeTextRect,
                compact ? $"{Mathf.RoundToInt(currentVolume * 100f)}%" : $"VOL {Mathf.RoundToInt(currentVolume * 100f)}%");

            if (Event.current != null &&
                Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                volumeSliderRect.Contains(mouseGui))
            {
                _playbackVolumeDragging = true;
            }

            float nextVolume = HelpSlider(volumeSliderRect, currentVolume, 0f, 1f, "動画音量");
            if (Mathf.Abs(nextVolume - currentVolume) > 0.0001f)
            {
                if (_vmdBarMode) VmdBarCommand("volume", value: nextVolume);
                else ApplyPlaybackBarVolume(nextVolume, persist: false);
            }

            string currentPreset = string.IsNullOrWhiteSpace(_settings.VoiceReverbPreset) ? "Off" : _settings.VoiceReverbPreset;
            if (HelpButton(_reverbDropdownButtonRect, currentPreset, "残響プリセットを選択"))
            {
                _reverbDropdownOpen = !_reverbDropdownOpen;
            }

            bool currentVideoReverb = _settings.ApplyReverbToVideoAudio;
            bool nextVideoReverb = HelpToggle(videoReverbToggleRect, currentVideoReverb, "V-REV", "動画音に残響を適用");
            if (nextVideoReverb != currentVideoReverb)
            {
                ApplyPlaybackBarVideoReverbToggle(nextVideoReverb, persist: true);
            }

            bool canSeek = _vmdBarMode ? (bool)_vmdBarState[1] && totalSec > 0 : player != null && player.canSetTime && totalSec > 0.0001d;
            var seekEvent = Event.current;
            bool seekDown = canSeek && seekEvent.type == EventType.MouseDown && seekEvent.button == 0 && seekSliderRect.Contains(seekEvent.mousePosition);
            int seekControl = GUIUtility.GetControlID(178341, FocusType.Passive, seekSliderRect);
            if (seekDown) {
                _playbackSeekDragging = true;
                _playbackSeekControl = seekControl;
                GUIUtility.hotControl = seekControl;
            }
            bool seekRelease = _playbackSeekDragging && (seekEvent.rawType == EventType.MouseUp || !Input.GetMouseButton(0)) && !seekDown;
            if (!_playbackSeekDragging && canSeek && Time.unscaledTime >= _playbackSeekHoldUntil)
                _playbackSeekNormalized = Mathf.Clamp01((float)(currentSec / totalSec));
            float prevNorm = _playbackSeekNormalized;
            float nextNorm = prevNorm;
            if (_playbackSeekDragging && canSeek) {
                nextNorm = Mathf.Clamp01((seekEvent.mousePosition.x - seekSliderRect.x) / Mathf.Max(1, seekSliderRect.width));
                _playbackSeekHoldUntil = Time.unscaledTime + 0.5f;
            }
            // Rendering never owns the gesture or overwrites the pointer's requested position.
            TrackHelp(seekSliderRect, "再生位置シーク");
            bool seekGuiEnabled = GUI.enabled;
            GUI.enabled = canSeek;
            if (seekEvent.type == EventType.Repaint) {
                GUI.skin.horizontalSlider.Draw(seekSliderRect, GUIContent.none, false, _playbackSeekDragging, false, false);
                float thumbWidth = Mathf.Max(8f, GUI.skin.horizontalSliderThumb.fixedWidth);
                float thumbHeight = Mathf.Max(12f, GUI.skin.horizontalSliderThumb.fixedHeight);
                var thumb = new Rect(seekSliderRect.x + nextNorm * (seekSliderRect.width - thumbWidth), seekSliderRect.center.y - thumbHeight / 2, thumbWidth, thumbHeight);
                GUI.skin.horizontalSliderThumb.Draw(thumb, GUIContent.none, thumb.Contains(mouseGui), _playbackSeekDragging, false, false);
            }
            GUI.enabled = seekGuiEnabled;
            if (_playbackSeekDragging && (seekEvent.type == EventType.MouseDown || seekEvent.type == EventType.MouseDrag || seekEvent.type == EventType.MouseUp)) seekEvent.Use();
            if (seekRelease || !canSeek) {
                _playbackSeekDragging = false;
                if (GUIUtility.hotControl == _playbackSeekControl) GUIUtility.hotControl = 0;
                _playbackSeekControl = 0;
            }

            if (canSeek && Mathf.Abs(nextNorm - prevNorm) > 0.0001f)
            {
                _playbackSeekNormalized = nextNorm;
                double target = totalSec * nextNorm;
                if (!double.IsNaN(target) && !double.IsInfinity(target))
                {
                    if (_vmdBarMode) VmdBarCommand("seek", value: (float)target);
                    else player.time = Mathf.Clamp((float)target, 0f, (float)totalSec);
                    currentSec = target;
                }
            }

            if (_reverbDropdownOpen)
            {
                const float rowHeight = 20f;
                const float listPad = 3f;
                int visibleCount = Mathf.Clamp(ReverbPresets.Length, 1, 10);
                float listHeight = visibleCount * rowHeight + listPad * 2f;
                float listY = Mathf.Max(2f, _reverbDropdownButtonRect.y - listHeight - 2f);
                _reverbDropdownListRect = new Rect(
                    _reverbDropdownButtonRect.x,
                    listY,
                    _reverbDropdownButtonRect.width,
                    listHeight);
                GUI.Box(_reverbDropdownListRect, GUIContent.none);

                var viewport = new Rect(
                    _reverbDropdownListRect.x + 2f,
                    _reverbDropdownListRect.y + 2f,
                    _reverbDropdownListRect.width - 4f,
                    _reverbDropdownListRect.height - 4f);
                var viewRect = new Rect(0f, 0f, Mathf.Max(24f, viewport.width - 18f), ReverbPresets.Length * rowHeight);
                _reverbDropdownScroll = GUI.BeginScrollView(viewport, _reverbDropdownScroll, viewRect, false, true);
                for (int i = 0; i < ReverbPresets.Length; i++)
                {
                    string preset = ReverbPresets[i];
                    bool selected = string.Equals(preset, currentPreset, StringComparison.OrdinalIgnoreCase);
                    string itemLabel = selected ? $"> {preset}" : preset;
                    var rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 1f);
                    if (!GUI.Button(rowRect, itemLabel))
                        continue;
                    ApplyReverbPresetFromBar(preset);
                    _reverbDropdownOpen = false;
                }
                GUI.EndScrollView();
            }

            if (showRoomControls)
                DrawEmbeddedPlaybackUi(new Rect(barRect.x + 4f, barRect.y + 4f, barRect.width - 8f, Mathf.Max(1f, expandedHeight - 8f)), DrawEmbeddedRoom);
            else
                ClearEmbeddedBarInteraction();

            DrawActorCoordinatesPopup();
            DrawRoomOverlayPopup();
            DrawPlaybackBarHelpPopup(mouseGui, _vmdHoveredHelp ?? hoveredHelpText);

            // IMGUI側のイベント消費
            if (Event.current != null)
            {
                bool isMouseEvent = Event.current.type == EventType.MouseDown
                    || Event.current.type == EventType.MouseUp
                    || Event.current.type == EventType.MouseDrag
                    || Event.current.type == EventType.ScrollWheel;
                if (isMouseEvent)
                {
                    bool consumed = barRect.Contains(mouseGui) || _actorCoordinatesList.Contains(mouseGui) || _roomOverlayListRect.Contains(mouseGui);
                    if (!consumed && folderDropdownListRect.width > 0f && folderDropdownListRect.Contains(mouseGui))
                        consumed = true;
                    if (!consumed && videoDropdownListRect.width > 0f && videoDropdownListRect.Contains(mouseGui))
                        consumed = true;
                    if (!consumed && _reverbDropdownListRect.width > 0f && _reverbDropdownListRect.Contains(mouseGui))
                        consumed = true;
                    if (consumed)
                    {
                        Event.current.Use();
                    }
                }
            }

            // uGUI EventSystem側のクリック貫通防止（透明ブロッカー）
            _playbackInputRects[0] = barRect;
            _playbackInputRects[1] = _actorCoordinatesOpen ? _actorCoordinatesList : Rect.zero;
            _playbackInputRects[2] = folderDropdownListRect;
            _playbackInputRects[3] = videoDropdownListRect;
            _playbackInputRects[4] = _reverbDropdownListRect;
            _playbackInputRects[5] = _roomOverlayDropdownOpen ? _roomOverlayListRect : Rect.zero;
            _uiResources.UpdateBlocker(_playbackInputRects);
        }

        private int _playbackSeekControl;
        private int _playbackSliderHotControl;
        private float _playbackSeekHoldUntil;
        private readonly Rect[] _playbackInputRects = new Rect[6];

        private static readonly string[] ReverbPresets =
        {
            "Off", "Generic", "PaddedCell", "Room", "Bathroom", "Livingroom",
            "Stoneroom", "Auditorium", "Concerthall", "Cave", "Arena", "Hangar",
            "CarpetedHallway", "Hallway", "StoneCorridor", "Alley", "Forest",
            "City", "Mountains", "Quarry", "Plain", "ParkingLot", "SewerPipe",
            "Underwater", "Drugged", "Dizzy", "Psychotic"
        };

        private void ApplyReverbPresetFromBar(string preset)
        {
            _settings.VoiceReverbPreset = preset;
            _settings.EnableVoiceReverb = !string.Equals(preset, "Off", StringComparison.OrdinalIgnoreCase);
            ApplyGlobalReverbState("reverb-preset-bar", persistConfig: true);
            if (_cfgVoiceReverbPreset != null)
                _cfgVoiceReverbPreset.Value = preset;
        }

        private void DrawPlaybackBarHelpPopup(Vector2 mouseGui, string hoveredHelpText)
        {
            if (_settings == null || !_settings.EnableUiHelpPopup)
                return;

            string tip = !string.IsNullOrWhiteSpace(hoveredHelpText) ? hoveredHelpText : GUI.tooltip;
            if (string.IsNullOrWhiteSpace(tip))
                return;

            var content = new GUIContent(tip);
            Vector2 textSize = GUI.skin.box.CalcSize(content);
            float width = Mathf.Clamp(textSize.x + 14f, 200f, Screen.width - 16f);
            float height = Mathf.Max(24f, textSize.y + 8f);
            float x = mouseGui.x + 14f;
            float y = mouseGui.y + 20f;
            if (x + width > Screen.width - 8f)
                x = Screen.width - width - 8f;
            if (y + height > Screen.height - 8f)
                y = mouseGui.y - height - 8f;
            if (x < 8f) x = 8f;
            if (y < 8f) y = 8f;

            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.96f);
            GUI.Box(new Rect(x, y, width, height), content);
            GUI.color = prev;
        }

        private void ApplyPlaybackBarHelpPopupToggle(bool enabled, bool persist)
        {
            if (_settings == null)
                return;

            _settings.EnableUiHelpPopup = enabled;

            if (!persist)
                return;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgEnableUiHelpPopup != null)
                    _cfgEnableUiHelpPopup.Value = enabled;
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);
        }

        private void ApplyPlaybackBarVolume(float volume, bool persist)
        {
            float clamped = Mathf.Clamp01(volume);
            _settings.VideoVolume = clamped;
            ApplyRuntimeVideoAudioLevel(clamped);

            if (!persist)
                return;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgVideoVolume != null)
                {
                    _cfgVideoVolume.Value = clamped;
                }
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);
        }

        private void CommitPlaybackBarVolume()
        {
            ApplyPlaybackBarVolume(_settings.VideoVolume, persist: true);
        }

        private void ApplyPlaybackBarVideoGain(float gain, bool persist)
        {
            if (_settings == null)
                return;

            float clamped = Mathf.Clamp(gain, 0.1f, 6f);
            _settings.VideoAudioGain = clamped;
            _videoGainInput = FormatRoomNumeric(clamped);
            ApplyRuntimeVideoAudioLevel(_settings.VideoVolume);

            if (!persist)
                return;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgVideoAudioGain != null)
                    _cfgVideoAudioGain.Value = clamped;
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);
        }

        private void CommitPlaybackBarVideoGain()
        {
            if (_settings == null)
                return;
            ApplyPlaybackBarVideoGain(_settings.VideoAudioGain, persist: true);
        }


        private void ApplyPlaybackBarVideoReverbToggle(bool enabled, bool persist)
        {
            if (_settings == null)
                return;

            _settings.ApplyReverbToVideoAudio = enabled;
            if (_videoRoomAudioSource != null)
                ConfigureVideoRoomAudioSource(_videoRoomAudioSource);

            if (!persist)
                return;

            bool prevSync = _syncingConfig;
            _syncingConfig = true;
            try
            {
                if (_cfgApplyReverbToVideoAudio != null)
                    _cfgApplyReverbToVideoAudio.Value = enabled;
            }
            finally
            {
                _syncingConfig = prevSync;
                _configDirty = false;
            }

            _settingsRepository.SaveCurrent(_settings);
        }

        private static float MapReverbStrengthToMix(float normalized)
        {
            float n = Mathf.Clamp01(normalized);
            // Ease-in curve: low range stays gentle, high range grows stronger.
            float curved = Mathf.Pow(n, 2.1f);
            return Mathf.Lerp(0f, 1.1f, curved);
        }

        private void ApplyPlaybackBarReverbMixToPlayingSources(float normalized)
        {
            float mix = MapReverbStrengthToMix(normalized);
            bool disable = mix <= 0.0001f || !(_settings?.EnableVoiceReverb ?? false);
            var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            for (int i = 0; i < sources.Length; i++)
            {
                var s = sources[i];
                if (s == null || !s.isPlaying) continue;
                if (disable)
                {
                    s.bypassReverbZones = true;
                    s.reverbZoneMix = 0f;
                    continue;
                }

                if (s.spatialBlend <= 0.01f) continue;
                s.bypassReverbZones = false;
                s.reverbZoneMix = mix;
            }
        }

        private void ApplyGlobalReverbState(string source, bool persistConfig)
        {
            if (_settings == null)
                return;

            bool enabled = _settings.EnableVoiceReverb;

            if (_videoRoomRoot != null || _voiceReverbZone != null || _reverbZoneObject != null)
                ApplyRoomReverb(_settings.RoomWidth, _settings.RoomDepth, _settings.RoomHeight);

            ApplyPlaybackBarReverbMixToPlayingSources(enabled ? 1f : 0f);
            if (_videoRoomAudioSource != null)
                ConfigureVideoRoomAudioSource(_videoRoomAudioSource);

            if (persistConfig)
            {
                bool prevSync = _syncingConfig;
                _syncingConfig = true;
                try
                {
                    if (_cfgEnableVoiceReverb != null)
                        _cfgEnableVoiceReverb.Value = enabled;
                }
                finally
                {
                    _syncingConfig = prevSync;
                    _configDirty = false;
                }
            }

            LogInfo($"[reverb-state] source={source} enabled={enabled}");
        }

        private void EnforceReverbBypassWhileDisabled()
        {
            if (_settings == null || _settings.EnableVoiceReverb)
                return;
            if (Time.unscaledTime < _nextReverbBypassEnforceTime)
                return;

            _nextReverbBypassEnforceTime = Time.unscaledTime + 0.25f;
            ApplyPlaybackBarReverbMixToPlayingSources(0f);
            if (_videoRoomAudioSource != null)
                ConfigureVideoRoomAudioSource(_videoRoomAudioSource);
        }

        private static double ResolveTotalSeconds(VideoPlayer player)
        {
            if (player == null) return 0d;
            double len = player.length;
            if (!double.IsNaN(len) && !double.IsInfinity(len) && len > 0d)
                return len;

            if (player.frameCount > 0ul && player.frameRate > 0.0001f)
                return player.frameCount / player.frameRate;

            return 0d;
        }

        private static double ResolveCurrentSeconds(VideoPlayer player, double totalSec)
        {
            if (player == null) return 0d;

            double time = player.time;
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0d)
            {
                if (player.frame >= 0L && player.frameRate > 0.0001f)
                    time = player.frame / player.frameRate;
                else
                    time = 0d;
            }

            if (totalSec > 0d && time > totalSec) time = totalSec;
            if (time < 0d) time = 0d;
            return time;
        }

        private static long ResolveTotalFrame(VideoPlayer player, double totalSec)
        {
            if (player == null) return 0L;
            if (player.frameCount > 0ul && player.frameCount <= long.MaxValue)
                return (long)player.frameCount;

            if (totalSec > 0d && player.frameRate > 0.0001f)
                return (long)Math.Round(totalSec * player.frameRate);

            return 0L;
        }

        private static long ResolveCurrentFrame(VideoPlayer player, long totalFrame)
        {
            if (player == null) return 0L;
            long f = player.frame;
            if (f < 0L) f = 0L;
            if (totalFrame > 0L && f > totalFrame) f = totalFrame;
            return f;
        }

        private static string FormatSeconds(double sec)
        {
            if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0d)
                return "--:--";

            int all = (int)Math.Floor(sec + 0.0001d);
            int h = all / 3600;
            int m = (all % 3600) / 60;
            int s = all % 60;
            return h > 0 ? $"{h:00}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }

        private static string FormatFrame(long frame)
        {
            return frame > 0L ? frame.ToString() : "0";
        }
    }
}
