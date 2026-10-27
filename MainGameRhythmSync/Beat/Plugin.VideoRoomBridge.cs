using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MainGameBeatSyncSpeed
{
    public partial class Plugin
    {
        private delegate bool SnapshotGetter(out double timeSec, out double lengthSec,
            out bool isPrepared, out bool isPlaying);
        private static SnapshotGetter _snapshotGetter;
        private static Type         _blankMapPluginType;
        private static bool         _blankMapLookupDone;
        private static PropertyInfo _blankMapInstanceProp;
        private static FieldInfo    _mainVideoPlayerField;
        private float _lastVideoSnapshotAt = float.NegativeInfinity;
        private float _nextVideoPathChangeCheckTime;

        // ── 毎フレーム呼び出し ────────────────────────────────────────
        private void PollVideoRoom()
        {
            if (Time.unscaledTime < _nextVideoRoomPollTime) return;
            _nextVideoRoomPollTime = Time.unscaledTime + 0.05f; // 20fps

            if (TryGetSnapshot(out double timeSec, out bool isPlaying))
            {
                bool wasPlaying = _videoRoomPlaying;
                _videoRoomTimeSec   = timeSec;
                _videoRoomPlaying   = isPlaying;
                _lastVideoSnapshotAt = Time.unscaledTime;

                if (!_videoRoomFetchWasAvailable ||
                    (!wasPlaying && isPlaying))
                {
                    LogInfo($"[video-bridge] ok playing={isPlaying} time={timeSec:0.###}");
                }
                _videoRoomFetchWasAvailable = true;
            }
            else
            {
                bool hadAvailable = _videoRoomFetchWasAvailable;
                ResetVideoRoom();
                if (hadAvailable &&
                    Time.unscaledTime >= _nextVideoRoomErrorLogTime)
                {
                    _nextVideoRoomErrorLogTime = Time.unscaledTime + 2f;
                    LogInfo("[video-bridge] snapshot unavailable");
                }
            }
        }

        private void ResetVideoRoom()
        {
            _videoRoomTimeSec           = 0d;
            _videoRoomPlaying           = false;
            _videoRoomFetchWasAvailable = false;
            _lastVideoSnapshotAt        = float.NegativeInfinity;
        }

        // ── リフレクション経由でスナップショット取得 ──────────────────
        private bool TryGetSnapshot(out double timeSec, out bool isPlaying)
        {
            timeSec   = 0d;
            isPlaying = false;

            if (CanonPlugins.Shared.VmdRhythmSource.TryRead(out _, out timeSec, out isPlaying)) return true;
            if (!ResolveSnapshotMethod()) return false;

            try
            {
                return _snapshotGetter(out timeSec, out _, out _, out isPlaying);
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= _nextVideoRoomErrorLogTime)
                {
                    _nextVideoRoomErrorLogTime = Time.unscaledTime + 2f;
                    LogWarn("video-bridge invoke failed: " + ex.Message);
                }
                return false;
            }
        }

        private static MethodInfo _blankMapGetVideoPathMethod;
        private static Func<string> _blankMapGetVideoPath;
        private static bool      _blankMapGetVideoPathLookupDone;
        private static bool      _legacyInstanceRouteWarned;

        // ── 動画ファイルパス取得 ───────────────────────────────────────
        //
        // BlankMapAdd は静的な Instance を廃止した（テストで存在しないことを検査しているほど明確な設計変更）。
        // そのため Instance 経由で内部フィールドを覗く旧経路は必ず失敗し、曲が特定できなくなっていた。
        // 現在の正しい窓口は公開メソッド GetCurrentVideoPath()。まずそちらを使い、
        // 見つからない場合だけ旧経路に落ちる。
        internal bool TryGetVideoFilePath(out string videoPath)
        {
            videoPath = null;
            if (CanonPlugins.Shared.VmdRhythmSource.TryRead(out videoPath, out _, out _)) return !string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath);
            if (!ResolveSnapshotMethod()) return false; // ensures _blankMapPluginType is resolved

            string url = TryGetVideoUrlFromApi() ?? TryGetVideoUrlFromLegacyInstance();
            if (string.IsNullOrEmpty(url)) return false;

            // "file:///F:/..." → "F:/..."
            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                url = Uri.UnescapeDataString(url.Substring(8));

            videoPath = url.Replace('/', Path.DirectorySeparatorChar);
            return File.Exists(videoPath);
        }

        /// <summary>公開API GetCurrentVideoPath() から取る。これが現行の正規ルート。</summary>
        private string TryGetVideoUrlFromApi()
        {
            if (!_blankMapGetVideoPathLookupDone)
            {
                _blankMapGetVideoPathLookupDone = true;
                _blankMapGetVideoPathMethod = _blankMapPluginType.GetMethod(
                    "GetCurrentVideoPath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (_blankMapGetVideoPathMethod != null)
                {
                    try
                    {
                        _blankMapGetVideoPath = (Func<string>)Delegate.CreateDelegate(
                            typeof(Func<string>), _blankMapGetVideoPathMethod);
                    }
                    catch (ArgumentException)
                    {
                        _blankMapGetVideoPathMethod = null;
                    }
                }

                LogInfo(_blankMapGetVideoPathMethod != null
                    ? "[video-bridge] using BlankMapAdd.GetCurrentVideoPath()"
                    : "[video-bridge] GetCurrentVideoPath() not found — falling back to legacy Instance route");
            }

            if (_blankMapGetVideoPath == null) return null;

            try { return _blankMapGetVideoPath(); }
            catch (Exception ex)
            {
                LogWarn("[video-bridge] GetCurrentVideoPath failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>旧経路。静的 Instance が残っている版の BlankMapAdd 用。</summary>
        private string TryGetVideoUrlFromLegacyInstance()
        {
            if (_blankMapInstanceProp == null)
            {
                _blankMapInstanceProp = _blankMapPluginType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (_blankMapInstanceProp == null)
                {
                    if (!_legacyInstanceRouteWarned)
                    {
                        _legacyInstanceRouteWarned = true;
                        LogWarn("[video-bridge] BlankMapAdd has no static Instance (by design) and "
                            + "GetCurrentVideoPath() was unavailable — cannot resolve the current song");
                    }
                    return null;
                }
            }

            object inst = _blankMapInstanceProp.GetValue(null, null);
            if (inst == null) return null;

            if (_mainVideoPlayerField == null)
            {
                _mainVideoPlayerField = _blankMapPluginType.GetField(
                    "_mainVideoPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_mainVideoPlayerField == null) return null;
            }

            object player = _mainVideoPlayerField.GetValue(inst);
            if (player == null) return null;

            PropertyInfo urlProp = player.GetType().GetProperty("url");
            return urlProp?.GetValue(player, null) as string;
        }

        private bool ResolveSnapshotMethod()
        {
            if (_snapshotGetter != null) return true;
            if (_blankMapLookupDone)             return false;

            _blankMapLookupDone = true;

            _blankMapPluginType =
                AccessTools.TypeByName("MainGameBlankMapAdd.Plugin") ??
                Type.GetType("MainGameBlankMapAdd.Plugin, MainGameBlankMapAdd");

            if (_blankMapPluginType == null)
            {
                LogWarn("MainGameBlankMapAdd.Plugin not found — video-bridge disabled");
                return false;
            }

            MethodInfo snapshotMethod = _blankMapPluginType.GetMethod(
                "TryGetMainVideoPlaybackSnapshot",
                BindingFlags.Public | BindingFlags.Static);

            if (snapshotMethod == null)
            {
                LogWarn("TryGetMainVideoPlaybackSnapshot not found");
                return false;
            }

            try
            {
                _snapshotGetter = (SnapshotGetter)Delegate.CreateDelegate(
                    typeof(SnapshotGetter), snapshotMethod);
            }
            catch (ArgumentException ex)
            {
                LogWarn("TryGetMainVideoPlaybackSnapshot signature mismatch: " + ex.Message);
                return false;
            }

            LogInfo("video-bridge connected: MainGameBlankMapAdd.Plugin.TryGetMainVideoPlaybackSnapshot");
            return true;
        }
    }
}
