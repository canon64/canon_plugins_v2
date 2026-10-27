using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace MainGameClubLights
{
    // 体位ごとに「丸ごとスナップショット」（フォルダ位置・回転＋全ライト全設定）を保存し、
    // 体位が変わったら自動で復元する。MGCC の体位保存と同じ思想（明示保存＋スナップショット）。
    public sealed partial class Plugin
    {
        private string _lastPoseKey = string.Empty;
        private bool   _poseTrackingInitialized;
        private int _cachedPoseMode = int.MinValue;
        private int _cachedPoseId = int.MinValue;
        private string _cachedPoseKey = string.Empty;
        private string _cachedPoseRawName;
        private string _cachedPoseDisplayName = string.Empty;

        private bool TryGetCurrentPoseInfo(out string key, out string displayName)
        {
            key = string.Empty;
            displayName = string.Empty;
            try
            {
                HSceneProc proc = _hSceneProc != null ? _hSceneProc : FindObjectOfType<HSceneProc>();
                if (proc == null || proc.flags == null || proc.flags.nowAnimationInfo == null)
                    return false;

                HSceneProc.AnimationListInfo info = proc.flags.nowAnimationInfo;
                int mode = (int)info.mode;
                int id = info.id;
                bool poseChanged = mode != _cachedPoseMode || id != _cachedPoseId;
                if (poseChanged)
                {
                    _cachedPoseMode = mode;
                    _cachedPoseId = id;
                    _cachedPoseKey = info.mode.ToString() + ":" + info.id.ToString();
                    _cachedPoseRawName = null;
                }
                if (poseChanged || !string.Equals(_cachedPoseRawName, info.nameAnimation, StringComparison.Ordinal))
                {
                    _cachedPoseRawName = info.nameAnimation;
                    _cachedPoseDisplayName = string.IsNullOrWhiteSpace(info.nameAnimation)
                        ? _cachedPoseKey : info.nameAnimation;
                }
                key = _cachedPoseKey;
                displayName = _cachedPoseDisplayName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<LightInstanceSettings> CloneLights(List<LightInstanceSettings> src)
        {
            var result = new List<LightInstanceSettings>();
            if (src == null) return result;
            var ser = new DataContractJsonSerializer(typeof(LightInstanceSettings));
            foreach (var li in src)
            {
                if (li == null) continue;
                using (var ms = new MemoryStream())
                {
                    ser.WriteObject(ms, li);
                    ms.Position = 0;
                    result.Add((LightInstanceSettings)ser.ReadObject(ms));
                }
            }
            return result;
        }

        private static List<LightFolder> CloneFolders(List<LightFolder> src)
        {
            var result = new List<LightFolder>();
            if (src == null) return result;
            var ser = new DataContractJsonSerializer(typeof(LightFolder));
            foreach (var f in src)
            {
                if (f == null) continue;
                using (var ms = new MemoryStream())
                {
                    ser.WriteObject(ms, f);
                    ms.Position = 0;
                    result.Add((LightFolder)ser.ReadObject(ms));
                }
            }
            return result;
        }

        private LightPoseSnapshot FindPoseSnapshot(string key)
        {
            if (string.IsNullOrEmpty(key) || _settings.LightPoseSnapshots == null) return null;
            foreach (var s in _settings.LightPoseSnapshots)
                if (s != null && string.Equals(s.Key, key, StringComparison.Ordinal))
                    return s;
            return null;
        }

        // UI「体位を上書き」: 現在の体位として、フォルダ位置＋全ライト設定を丸ごと保存
        internal void CaptureCurrentPoseSnapshot()
        {
            if (!TryGetCurrentPoseInfo(out string key, out string displayName))
            {
                _log.Info("[Pose] 保存スキップ: 体位を取得できない（Hシーン外）");
                return;
            }

            if (_settings.LightPoseSnapshots == null)
                _settings.LightPoseSnapshots = new List<LightPoseSnapshot>();

            var snap = FindPoseSnapshot(key);
            if (snap == null)
            {
                snap = new LightPoseSnapshot { Key = key };
                _settings.LightPoseSnapshots.Add(snap);
            }

            // 現在のフォルダ transform を設定へ反映してからスナップショット
            UpdateFolders();
            snap.DisplayName = displayName ?? string.Empty;
            snap.Folders = CloneFolders(_settings.Folders);
            snap.Lights  = CloneLights(_settings.Lights);

            SaveSettingsNow("pose-snapshot-save");
            _log.Info($"[Pose] スナップショット保存 key={key} disp={displayName} lights={snap.Lights.Count}");
        }

        // スナップショットを丸ごと復元（フォルダ位置＋全ライトを差し替えて再構築）
        private void ApplyPoseSnapshot(LightPoseSnapshot snap)
        {
            if (snap == null) return;

            _settings.Folders = CloneFolders(snap.Folders);
            _settings.Lights  = CloneLights(snap.Lights);
            EnsureSettingsValid();

            DestroyAllLights();
            if (_insideHScene)
                BuildLightObjects();

            _log.Info($"[Pose] スナップショット適用 key={snap.Key} lights={_settings.Lights.Count}");
        }

        private void ApplyPoseSnapshotForKey(string key)
        {
            var snap = FindPoseSnapshot(key);
            if (snap == null) return;
            ApplyPoseSnapshot(snap);
            _log.Info($"[Pose] 自動適用 key={key}");
        }

        internal void RemovePoseSnapshot(int index)
        {
            if (_settings.LightPoseSnapshots == null) return;
            if (index < 0 || index >= _settings.LightPoseSnapshots.Count) return;
            string removed = _settings.LightPoseSnapshots[index]?.DisplayName ?? string.Empty;
            _settings.LightPoseSnapshots.RemoveAt(index);
            SaveSettingsNow("pose-snapshot-remove");
            _log.Info($"[Pose] スナップショット削除 disp={removed}");
        }

        // 体位変化を監視し、auto-apply ON時にスナップショットを復元
        private void UpdatePoseTracking()
        {
            bool hasPose = TryGetCurrentPoseInfo(out string key, out _);
            if (!hasPose)
            {
                _lastPoseKey = string.Empty;
                _poseTrackingInitialized = false;
                return;
            }

            if (!_settings.PosePresetAutoApply)
            {
                _lastPoseKey = key;
                _poseTrackingInitialized = true;
                return;
            }

            if (!_poseTrackingInitialized)
            {
                _lastPoseKey = key;
                _poseTrackingInitialized = true;
                ApplyPoseSnapshotForKey(key);
                return;
            }

            if (string.Equals(key, _lastPoseKey, StringComparison.Ordinal))
                return;

            _lastPoseKey = key;
            ApplyPoseSnapshotForKey(key);
        }
    }
}
