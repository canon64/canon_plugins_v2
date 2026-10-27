using System;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private string _audioVolumeText;
        private string _reverbStatus = "残響: 音源未読込";
        private MethodInfo _registerReverb, _unregisterReverb;
        private static float NormalizeAudioVolume(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
        private void ApplyMusicOutput()
        {
            if (_music == null) return;
            _settings.AudioVolume = NormalizeAudioVolume(_settings.AudioVolume);
            _music.volume = _settings.AudioVolume;
            _music.bypassReverbZones = true;
            _music.reverbZoneMix = 0;
            try {
                if (_registerReverb == null && Chainloader.PluginInfos.TryGetValue("com.kks.maingameblankmapadd", out var info)) {
                    _registerReverb = info.Instance.GetType().GetMethod("RegisterExternalMusicSource", BindingFlags.Public | BindingFlags.Static);
                    _unregisterReverb = info.Instance.GetType().GetMethod("UnregisterExternalMusicSource", BindingFlags.Public | BindingFlags.Static);
                }
                bool ok = _registerReverb != null && (bool)_registerReverb.Invoke(null, new object[] { Guid, _music });
                _reverbStatus = ok ? "残響: V-REVチェック時のみ、BlankMapAddの残響設定に連動" : "残響なし: BlankMapAdd連携が利用できません";
            } catch (Exception ex) { _reverbStatus = "残響なし: " + ex.GetBaseException().Message; }
        }
        private void UnregisterMusicReverb()
        {
            try { _unregisterReverb?.Invoke(null, new object[] { Guid }); }
            catch (Exception ex) { LogWarn("[Reverb] " + ex.GetBaseException().Message); }
        }
        private void DrawMusicVolume()
        {
            GUILayout.Label("プリセット音量（0～100%）");
            GUILayout.BeginHorizontal();
            float old = NormalizeAudioVolume(_settings.AudioVolume) * 100;
            float next = GUILayout.HorizontalSlider(old, 0, 100);
            string previous = _audioVolumeText ?? old.ToString("0.##", CultureInfo.InvariantCulture);
            string text = GUILayout.TextField(previous, GUILayout.Width(75));
            _audioVolumeText = text;
            if (text != previous && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && !float.IsNaN(number) && !float.IsInfinity(number)) next = Mathf.Clamp(number, 0, 100);
            if (next != old) {
                _settings.AudioVolume = next / 100;
                if (text == previous) _audioVolumeText = next.ToString("0.##", CultureInfo.InvariantCulture);
                if (_music != null) _music.volume = _settings.AudioVolume;
                QueueAdjustmentSave();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(_reverbStatus);
        }
    }
}
