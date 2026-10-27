using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private EventInfo _danceEvent;
        private PropertyInfo _danceEnabledSetting, _danceSuffixSetting;
        private Action<string, Func<int, string, Action, float>> _danceHandler;
        private void InitDanceTrigger()
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.kks.maingame.voicefaceeventbridge", out var info)) { LogWarn("[Dance] VoiceFaceEventBridge unavailable"); return; }
            _danceEvent = info.Instance.GetType().Assembly.GetType("MainGameVoiceFaceEventBridge.ResponseTextTriggerApi")?.GetEvent("Received");
            if (_danceEvent == null) { LogWarn("[Dance] response trigger API unavailable"); return; }
            _danceEnabledSetting = _danceEvent.DeclaringType.GetProperty("VmdDanceEnabled");
            _danceSuffixSetting = _danceEvent.DeclaringType.GetProperty("VmdDanceSuffix");
            _danceHandler = OnDanceText;
            _danceEvent.AddEventHandler(null, _danceHandler);
        }
        private void DisposeDanceTrigger() { if (_danceHandler != null) _danceEvent?.RemoveEventHandler(null, _danceHandler); _danceHandler = null; }
        private void PlayRandomDancePreset()
        {
            RefreshPresets();
            var candidates = _presetFiles.Where(path => {
                var preset = SettingsStore.Load(path);
                return File.Exists(preset.LastVmdAbsPath) &&
                    (string.IsNullOrEmpty(preset.LastAudioAbsPath) || File.Exists(preset.LastAudioAbsPath)) &&
                    (string.IsNullOrEmpty(preset.CameraVmdPath) || File.Exists(preset.CameraVmdPath));
            }).ToArray();
            if (candidates.Length == 0) {
                _presetMessage = "再生できるVMDプリセットがありません";
                LogWarn("[Dance] random: " + _presetMessage);
                return;
            }
            string path = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            ApplyPreset(path);
            LogInfo("[Dance] random preset=" + Path.GetFileNameWithoutExtension(path) + " result=" + _presetMessage);
        }
        private void OnDanceText(string text, Func<int, string, Action, float> schedule)
        {
            if (AstralBlocked) return;
            if (_danceEnabledSetting != null && !(bool)_danceEnabledSetting.GetValue(null, null)) return;
            string suffix = _danceSuffixSetting == null ? DanceTriggerMatcher.Suffix : ((string)_danceSuffixSetting.GetValue(null, null) ?? "").Trim();
            if (schedule == null || string.IsNullOrEmpty(text)) return;
            int poseEpoch = _poseEpoch;
            RefreshPresets();
            var names = _presetFiles.Select(Path.GetFileNameWithoutExtension).ToArray();
            foreach (var hit in DanceTriggerMatcher.Find(text, names, suffix)) {
                if (hit.IsRandom) {
                    schedule(hit.Index, "vmd_preset", () => {
                        if (this == null || poseEpoch != _poseEpoch || (_danceEnabledSetting != null && !(bool)_danceEnabledSetting.GetValue(null, null))) return;
                        PlayRandomDancePreset();
                    });
                    continue;
                }
                if (hit.Name == null) { LogWarn("[Dance] unknown preset at position=" + hit.Index); continue; }
                string path = _presetFiles[Array.IndexOf(names, hit.Name)];
                string name = hit.Name;
                schedule(hit.Index, "vmd_preset", () => {
                    if (this == null || poseEpoch != _poseEpoch || (_danceEnabledSetting != null && !(bool)_danceEnabledSetting.GetValue(null, null))) return;
                    ApplyPreset(path);
                    LogInfo("[Dance] preset=" + name + " result=" + _presetMessage);
                });
            }
        }
    }
}
