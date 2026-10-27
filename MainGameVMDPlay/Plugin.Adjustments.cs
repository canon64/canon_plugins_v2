using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private sealed class Adjustment {
            internal string Key, Label; internal float Min, Max; internal bool Toggle, Expand, Reload, Invert;
            internal Adjustment(string key, string label, float min = 0, float max = 1, bool toggle = false, bool expand = false, bool reload = false, bool invert = false)
            { Key = key; Label = label; Min = min; Max = max; Toggle = toggle; Expand = expand; Reload = reload; Invert = invert; }
        }
        private static readonly Adjustment[] Adjustments = {
            new Adjustment("char.speed", "再生速度", 0, 5, expand:true),
            new Adjustment("char.Loop", "モーションをループ", toggle:true),
            new Adjustment("char.AdjustSkirtBones", "スカート補正", toggle:true),
            new Adjustment("char.faceAnimeEnabled", "VMD表情", toggle:true),
            new Adjustment("char.enableIK", "足IK", toggle:true),
            new Adjustment("char.IKWeight.disableToeIK", "つま先IK", toggle:true, invert:true),
            new Adjustment("char.IKWeight.limitIKPositionFloor", "IKの床制限", toggle:true),
            new Adjustment("char.FaceOnly", "表情のみ（自動反映）", toggle:true, reload:true),
            new Adjustment("char.centerPosAdjust.y", "中心Y補正", -2, 2),
            new Adjustment("char.quickAdjust.ScaleModel", "モデル倍率", 0, 2, expand:true),
            new Adjustment("char.moveYScaleAdjust", "上下移動倍率", 0, 2),
            new Adjustment("char.centerBasePos.y", "中心Y基準（自動反映）", 0, 15, expand:true, reload:true),
            new Adjustment("char.quickAdjust.Shoulder", "肩角度（自動反映）", -10, 40, reload:true),
            new Adjustment("char.quickAdjust.ArmUp", "上腕角度（自動反映）", -10, 40, reload:true),
            new Adjustment("char.quickAdjust.ArmLow", "前腕角度（自動反映）", -10, 40, reload:true),
            new Adjustment("camera.CameraEnabled", "VMDカメラを使用", toggle:true),
            new Adjustment("camera.Speed", "カメラ速度", 0, 5, expand:true),
            new Adjustment("camera.Loop", "カメラをループ", toggle:true),
            new Adjustment("camera.modelScale", "カメラのモデル倍率", 0, 2, expand:true),
            new Adjustment("camera.cameraPosAdjust.x", "カメラX補正", -2, 2),
            new Adjustment("camera.cameraPosAdjust.y", "カメラY補正", -2, 2),
            new Adjustment("camera.cameraPosAdjust.z", "カメラZ補正", -2, 2),
            new Adjustment("camera.cameraDistanceAdjust", "カメラ距離補正", -2, 5),
            new Adjustment("camera.cameraRotAdjustY", "カメラY回転", -180, 180),
            new Adjustment("audio.Loop", "音源をループ", toggle:true),
            new Adjustment("audio.Speed", "音源速度", 0, 5, expand:true),
        };
        private readonly Dictionary<string, string> _adjustmentText = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _adjustmentMax = new Dictionary<string, float>();
        private readonly Dictionary<string, MemberInfo> _adjustmentMembers = new Dictionary<string, MemberInfo>();
        private float _adjustmentSaveAt = -1;
        private string _adjustmentError = "";
        private bool _showAdjustments = true;
        private float _audioSpeed = 1;
        private float _modelReloadAt = -1;
        private float _modelResumeTime = -1;
        private bool _modelReloadStarting;
        private void TickModelReload() {
            if (_modelReloadAt < 0 || Time.unscaledTime < _modelReloadAt || Input.GetMouseButton(0) || _loadRoutine != null) return;
            _modelReloadAt = -1;
            if (!_vmdOwnsCharacter || !System.IO.File.Exists(_settings.LastVmdAbsPath)) return;
            float time = VmdBridgeGetAnimTime(_controller);
            bool play = _state == PluginState.Playing;
            bool wasSwitching = _myListSwitching;
            _myListSwitching = true;
            _modelReloadStarting = true;
            _modelResumeTime = time;
            try { PlaybackLoadAndPlay(_settings.LastVmdAbsPath, play); }
            finally { _myListSwitching = wasSwitching; _modelReloadStarting = false; }
        }

        private bool DrawBinaryControl(string key, string label, bool value)
        {
            GUILayout.BeginHorizontal();
            bool next = GUILayout.Toggle(value, label);
            float slide = GUILayout.HorizontalSlider(value ? 1 : 0, 0, 1, GUILayout.Width(70));
            if (slide != (value ? 1 : 0)) next = slide >= 0.5f;
            string old = _adjustmentText.TryGetValue(key, out string cached) ? cached : (value ? "1" : "0");
            string text = GUILayout.TextField(old, GUILayout.Width(35));
            _adjustmentText[key] = text;
            if (text != old && (text == "0" || text == "1")) next = text == "1";
            if (next != value) _adjustmentText[key] = next ? "1" : "0";
            GUILayout.EndHorizontal();
            return next;
        }

        private void QueueAdjustmentSave() { _adjustmentSaveAt = Time.unscaledTime + 0.5f; }
        private void TickAdjustmentSave() { if (_adjustmentSaveAt >= 0 && Time.unscaledTime >= _adjustmentSaveAt) { _adjustmentSaveAt = -1; SaveSettings("adjustments"); } }
        private object CameraManager => _piMgrInstance == null ? null : AccessTools.Field(_tMgr, "CameraMgr")?.GetValue(_piMgrInstance.GetValue(null, null));
        private MemberInfo AdjustmentMember(Type type, string name) {
            string key = type.FullName + "." + name;
            if (!_adjustmentMembers.TryGetValue(key, out var member)) {
                member = (MemberInfo)AccessTools.Property(type, name) ?? AccessTools.Field(type, name);
                if (member == null) throw new MissingMemberException(key);
                _adjustmentMembers[key] = member;
            }
            return member;
        }
        private object ReadMember(object target, string name) { var m = AdjustmentMember(target.GetType(), name); return m is PropertyInfo p ? p.GetValue(target, null) : ((FieldInfo)m).GetValue(target); }
        private void WriteMember(object target, string name, object value) { var m = AdjustmentMember(target.GetType(), name); if (m is PropertyInfo p) p.SetValue(target, value, null); else ((FieldInfo)m).SetValue(target, value); }
        private object AdjustmentTarget(string key) => key.StartsWith("camera.") ? CameraManager : _controller;
        private float ReadAdjustment(Adjustment item) {
            if (item.Key == "camera.CameraEnabled") return _cameraEnabled ? 1 : 0;
            if (item.Key == "audio.Loop") return _settings.AudioLoop ? 1 : 0;
            if (item.Key == "audio.Speed") return _audioSpeed;
            object value = AdjustmentTarget(item.Key); if (value == null) throw new InvalidOperationException("キャラ準備待ち");
            var parts = item.Key.Split('.'); for (int i = 1; i < parts.Length; i++) value = ReadMember(value, parts[i]);
            return value is bool b ? (b ? 1 : 0) : Convert.ToSingle(value);
        }
        private void WritePath(object target, string[] parts, int index, float value) {
            object old = ReadMember(target, parts[index]);
            if (index + 1 == parts.Length) { WriteMember(target, parts[index], old is bool ? (object)(value != 0) : value); return; }
            WritePath(old, parts, index + 1, value);
            if (old.GetType().IsValueType) WriteMember(target, parts[index], old);
        }
        private void WriteAdjustment(Adjustment item, float value) {
            if (item.Key == "camera.CameraEnabled") { _cameraEnabled = value != 0; if (!_cameraEnabled) ReleaseCameraMotion(); else if (_state == PluginState.Playing) PlayCameraMotion(); return; }
            if (item.Key == "audio.Loop") { _settings.AudioLoop = value != 0; VmdBridgeSoundSetLoop(_settings.AudioLoop); return; }
            if (item.Key == "audio.Speed") { _audioSpeed = value; ApplyAudioSpeed(); return; }
            object target = AdjustmentTarget(item.Key); if (target == null) return;
            if (_myListRunning && (item.Key == "char.Loop" || item.Key == "camera.Loop")) value = 0;
            WritePath(target, item.Key.Split('.'), 1, value);
            if (item.Key == "char.faceAnimeEnabled") { if (value == 0) RestoreFaceControl(); else ResumeFaceControl(); }
        }
        private void ApplyRuntimeOptions() {
            if (_controller == null) return;
            WriteMember(_controller, "useIKDataInVMD", _settings.UseIKDataInVMD);
            WriteMember(_controller, "smoothArmTwistEnabled", _settings.SmoothArmTwist);
            WriteMember(_controller, "useBoneTransformUpdate", _settings.UseBoneTransform);
        }
        private void ApplyAudioSpeed() {
            if (_musicVideo != null) {
                if (_musicVideo.canSetPlaybackSpeed) _musicVideo.playbackSpeed = _audioSpeed;
                else if (_audioSpeed != 1) _adjustmentError = "この動画では再生速度の変更がサポートされていません";
            }
            if (_music != null && _musicVideo == null) _music.pitch = _audioSpeed;
        }
        private void ApplySavedAdjustments(bool includeReload) {
            ApplyRuntimeOptions();
            if (_myListRunning && _controller != null) WriteMember(_controller, "Loop", false);
            foreach (var item in Adjustments) if ((includeReload || !item.Reload) && _settings.Adjustments.TryGetValue(item.Key, out float value))
                try { WriteAdjustment(item, value); } catch (Exception ex) { _adjustmentError = item.Label + ": " + ex.GetBaseException().Message; }
        }
        private void CaptureAdjustmentValues() {
            foreach (var item in Adjustments) if (!_settings.Adjustments.ContainsKey(item.Key))
                try { _settings.Adjustments[item.Key] = ReadAdjustment(item); } catch { }
        }
        private void DrawAdjustments() {
            _showAdjustments = GUILayout.Toggle(_showAdjustments, "調節・Studio変形補正");
            if (!_showAdjustments) return;
            foreach (var item in Adjustments) {
                float value;
                try { value = _settings.Adjustments.TryGetValue(item.Key, out float stored) ? stored : ReadAdjustment(item); }
                catch (Exception ex) { GUILayout.Label(item.Label + "：未準備 / " + ex.GetBaseException().Message); continue; }
                float next = value;
                if (item.Toggle) {
                    bool shown = item.Invert ? value == 0 : value != 0;
                    bool selected = DrawBinaryControl(item.Key, item.Label, shown);
                    next = (item.Invert ? !selected : selected) ? 1 : 0;
                } else {
                    GUILayout.Label(item.Label);
                    GUILayout.BeginHorizontal();
                    float max = _adjustmentMax.TryGetValue(item.Key, out float m) ? m : item.Max;
                    float slid = GUILayout.HorizontalSlider(value, item.Min, max);
                    if (slid != Mathf.Clamp(value, item.Min, max)) next = slid;
                    string previous = _adjustmentText.TryGetValue(item.Key, out string text) ? text : value.ToString("0.####", CultureInfo.InvariantCulture);
                    string edited = GUILayout.TextField(previous, GUILayout.Width(85));
                    _adjustmentText[item.Key] = edited;
                    if (edited != previous && float.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed)) next = parsed;
                    if (slid != Mathf.Clamp(value, item.Min, max)) _adjustmentText[item.Key] = next.ToString("0.####", CultureInfo.InvariantCulture);
                    if (item.Expand) {
                        if (GUILayout.Button("0–1", GUILayout.Width(42))) _adjustmentMax[item.Key] = 1;
                        if (GUILayout.Button("0–10", GUILayout.Width(48))) _adjustmentMax[item.Key] = 10;
                        if (GUILayout.Button("×2", GUILayout.Width(34))) _adjustmentMax[item.Key] = max * 2;
                    }
                    GUILayout.EndHorizontal();
                }
                if (next != value) {
                    _settings.Adjustments[item.Key] = next;
                    if (!item.Reload) try { WriteAdjustment(item, next); } catch (Exception ex) { _adjustmentError = ex.GetBaseException().Message; }
                    if (item.Reload) _modelReloadAt = Time.unscaledTime + 0.6f;
                    QueueAdjustmentSave();
                }
            }
            DrawCameraMotion();
            DrawBodyCorrection();
            if (GUILayout.Button("調節値を反映してVMDを再読込") && System.IO.File.Exists(_settings.LastVmdAbsPath)) PlaybackLoadAndPlay(_settings.LastVmdAbsPath, false);
            if (!string.IsNullOrEmpty(_adjustmentError)) GUILayout.Label(_adjustmentError);
        }
    }
}
