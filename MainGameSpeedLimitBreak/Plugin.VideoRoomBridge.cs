using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private static readonly object[] VideoRoomSnapshotArgs = new object[4];
        private static Type _blankMapPluginType;
        private static MethodInfo _blankMapSnapshotMethod;
        private static bool _blankMapLookupDone;

        private void PollVideoRoomPlaybackSnapshot()
        {
            if (!ShouldPollVideoRoomBridge())
            {
                ResetVideoRoomPlaybackSnapshot();
                return;
            }

            if (Time.unscaledTime < _nextVideoRoomPollTime)
                return;

            _nextVideoRoomPollTime = Time.unscaledTime + 0.02f;

            if (TryGetVideoRoomPlaybackSnapshot(
                out double timeSec,
                out double lengthSec,
                out bool isPrepared,
                out bool isPlaying))
            {
                _videoRoomPlaybackAvailable = true;
                _videoRoomPlaybackTimeSec = timeSec;
                _videoRoomPlaybackLengthSec = lengthSec;
                _videoRoomPlaybackPrepared = isPrepared;
                _videoRoomPlaybackPlaying = isPlaying;

                if (!_videoRoomFetchWasAvailable || Time.unscaledTime >= _nextVideoRoomFetchLogTime)
                {
                    _nextVideoRoomFetchLogTime = Time.unscaledTime + 2f;
                    LogInfo(
                        $"[video-bridge] snapshot ok prepared={isPrepared} playing={isPlaying} " +
                        $"time={timeSec:0.###} length={lengthSec:0.###}");
                }

                _videoRoomFetchWasAvailable = true;
                return;
            }

            bool hadAvailable = _videoRoomFetchWasAvailable;
            ResetVideoRoomPlaybackSnapshot();
            if (hadAvailable)
            {
                LogInfo("[video-bridge] snapshot unavailable");
            }
        }

        private bool ShouldPollVideoRoomBridge()
        {
            var s = Settings;
            if (s == null || !s.EnableVideoTimeSpeedCues)
                return false;

            return _insideHScene && _hSceneProc != null;
        }

        private void ResetVideoRoomPlaybackSnapshot()
        {
            _videoRoomPlaybackAvailable = false;
            _videoRoomPlaybackTimeSec = 0d;
            _videoRoomPlaybackLengthSec = 0d;
            _videoRoomPlaybackPrepared = false;
            _videoRoomPlaybackPlaying = false;
            _videoRoomFetchWasAvailable = false;
        }

        private bool TryGetVideoRoomPlaybackSnapshot(
            out double timeSec,
            out double lengthSec,
            out bool isPrepared,
            out bool isPlaying)
        {
            timeSec = 0d;
            lengthSec = 0d;
            isPrepared = false;
            isPlaying = false;

            if (!ResolveBlankMapSnapshotMethod())
                return false;

            try
            {
                VideoRoomSnapshotArgs[0] = 0d;
                VideoRoomSnapshotArgs[1] = 0d;
                VideoRoomSnapshotArgs[2] = false;
                VideoRoomSnapshotArgs[3] = false;

                bool ok = (bool)_blankMapSnapshotMethod.Invoke(null, VideoRoomSnapshotArgs);
                if (!ok)
                    return false;

                timeSec = VideoRoomSnapshotArgs[0] is double t ? t : 0d;
                lengthSec = VideoRoomSnapshotArgs[1] is double l ? l : 0d;
                isPrepared = VideoRoomSnapshotArgs[2] is bool p && p;
                isPlaying = VideoRoomSnapshotArgs[3] is bool s && s;
                return true;
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= _nextVideoRoomInvokeErrorLogTime)
                {
                    _nextVideoRoomInvokeErrorLogTime = Time.unscaledTime + 2f;
                    LogWarn("video-room bridge invoke failed: " + ex.Message);
                }
                return false;
            }
        }

        private bool ResolveBlankMapSnapshotMethod()
        {
            if (_blankMapSnapshotMethod != null)
                return true;

            if (_blankMapLookupDone)
                return false;

            _blankMapLookupDone = true;
            _blankMapPluginType =
                AccessTools.TypeByName("MainGameBlankMapAdd.Plugin") ??
                Type.GetType("MainGameBlankMapAdd.Plugin, MainGameBlankMapAdd");

            if (_blankMapPluginType == null)
                return false;

            _blankMapSnapshotMethod = _blankMapPluginType.GetMethod(
                "TryGetMainVideoPlaybackSnapshot",
                BindingFlags.Public | BindingFlags.Static);

            if (_blankMapSnapshotMethod == null)
                return false;

            LogInfo("video-room bridge connected: MainGameBlankMapAdd.Plugin.TryGetMainVideoPlaybackSnapshot");
            return true;
        }
    }
}
