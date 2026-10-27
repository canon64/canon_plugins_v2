using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private Func<object[]> _clubLightsBarRead;
        private Action<int> _clubLightsBarStep;
        private Func<bool> _lightGizmoRead;
        private Action _lightGizmoToggle;
        private float _clubLightsBarNextResolve;
        private readonly Func<object[]>[] _cameraPresetReads = new Func<object[]>[2];
        private readonly Action<int>[] _cameraPresetCommands = new Action<int>[2];
        private float _cameraPresetResolveAt;
        private Func<bool> _subCameraGizmoRead;
        private Action _subCameraGizmoToggle;
        private Func<bool> _mainCameraCanSave;
        private Action _mainCameraSave;
        private Func<bool> _subCameraCanSave;
        private Action _subCameraSave;
        private GUIStyle _cameraSaveButtonStyle;

        private GUIStyle CameraSaveButtonStyle
        {
            get
            {
                if (_cameraSaveButtonStyle == null)
                    _cameraSaveButtonStyle = new GUIStyle(GUI.skin.button) {
                        padding = new RectOffset(0, 0, 0, 0),
                        alignment = TextAnchor.MiddleCenter,
                        contentOffset = Vector2.zero,
                        fontSize = 12,
                        fixedWidth = 0,
                        fixedHeight = 0,
                        wordWrap = false
                    };
                return _cameraSaveButtonStyle;
            }
        }

        private void DrawCameraPresetBar(Rect rect, int index, string title, string guid)
        {
            bool enabled = GUI.enabled;
            Color background = GUI.backgroundColor;
            try {
                if (_cameraPresetReads[index] == null && Time.unscaledTime >= _cameraPresetResolveAt && Chainloader.PluginInfos.TryGetValue(guid, out var info)) {
                    var type = info.Instance.GetType();
                    var read = type.GetMethod("GetPlaybackCameraPresetState");
                    var command = type.GetMethod("PlaybackCameraPresetCommand");
                    if (index == 0) {
                        var canSave = type.GetMethod("CanOverwritePlaybackCameraPose");
                        var save = type.GetMethod("OverwritePlaybackCameraPose");
                        if (canSave != null && save != null) {
                            _mainCameraCanSave = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, canSave);
                            _mainCameraSave = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, save);
                        }
                    }
                    if (index == 1) {
                        var cs = type.GetMethod("CanOverwritePlaybackCameraPose");
                        var ps = type.GetMethod("OverwritePlaybackCameraPose");
                        if (cs != null && ps != null) {
                            _subCameraCanSave = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, cs);
                            _subCameraSave = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, ps);
                        }
                        var gr = type.GetMethod("GetPlaybackCameraGizmo");
                        var gt = type.GetMethod("TogglePlaybackCameraGizmo");
                        if (gr != null && gt != null) {
                            _subCameraGizmoRead = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, gr);
                            _subCameraGizmoToggle = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, gt);
                        }
                    }
                    if (read != null && command != null) {
                        _cameraPresetReads[index] = (Func<object[]>)Delegate.CreateDelegate(typeof(Func<object[]>), info.Instance, read);
                        _cameraPresetCommands[index] = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), info.Instance, command);
                    }
                }
                var state = _cameraPresetReads[index]?.Invoke();
                float x = rect.x;
                GUI.Label(new Rect(x, rect.y + 2, 62, 20), title); x += 62;
                if (index == 1) {
                    GUI.enabled = enabled && _subCameraGizmoToggle != null;
                    bool shown = _subCameraGizmoRead?.Invoke() ?? false;
                    if (GUI.Toggle(new Rect(x, rect.y, 48, 21), shown, new GUIContent("ギズモ", "サブカメラの移動・回転ギズモを表示／非表示。離すと選択中プリセットへ上書き保存"), GUI.skin.button) != shown) _subCameraGizmoToggle();
                    x += 50;
                }
                GUI.enabled = enabled && state != null && (bool)state[0];
                if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("◁", title + "の前のプリセット"))) _cameraPresetCommands[index](-1);
                x += 26;
                bool automatic = state != null && (bool)state[1];
                GUI.backgroundColor = automatic ? Color.green : background;
                if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent(automatic ? "■" : "□", automatic ? "自動切替を停止" : "自動切替を開始"))) _cameraPresetCommands[index](0);
                GUI.backgroundColor = background; x += 26;
                if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("▷", title + "の次のプリセット"))) _cameraPresetCommands[index](1);
                GUI.enabled = enabled; x += 28;
                if (index == 0) {
                    GUI.enabled = enabled && _mainCameraSave != null && (_mainCameraCanSave?.Invoke() ?? false);
                    if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("◯", "表示中のメインカメラプリセットへ、現在体位の位置・角度・画角・注視設定を上書き保存"), CameraSaveButtonStyle)) _mainCameraSave();
                    x += 26; GUI.enabled = enabled;
                }
                if (index == 1) {
                    GUI.enabled = enabled && _subCameraSave != null && (_subCameraCanSave?.Invoke() ?? false);
                    if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("◯", "現在のメインカメラの構図をサブカメラプリセットへ上書き。呼出キャラ追従中は呼出キャラ用、それ以外は現在H体位へ保存"), CameraSaveButtonStyle)) _subCameraSave();
                    x += 26; GUI.enabled = enabled;
                }
                GUI.Label(new Rect(x, rect.y + 2, Mathf.Max(0, rect.xMax - x), 20), state == null ? "" : (string)state[2]);
            } catch (Exception ex) { _cameraPresetReads[index] = null; LogWarn("Camera preset bar: " + ex.Message); }
            finally { GUI.enabled = enabled; GUI.backgroundColor = background; }
        }

        private void DrawClubLightsPlaybackControls(Rect barRect, float rowY)
        {
            float x = barRect.x + 8f;
            float y = rowY;
            bool wrap = PlaybackTransportLayout.WrapPresetControls(barRect.width);
            var panel = new Rect(x, y - 2f, barRect.width - 16f, wrap ? 49f : 25f);
            float extra = Mathf.Max(0, panel.width - 824f) / 4f;
            float groupWidth = wrap ? panel.width / 2f : 160f + extra;
            float cameraWidth = wrap ? groupWidth : 171f + extra;
            float subCameraWidth = wrap ? groupWidth : 221f + extra;
            float actorWidth = wrap ? groupWidth : 272f + extra;
            DrawCameraPresetBar(new Rect(panel.x + groupWidth, y, cameraWidth, 23), 0, "カメラ", "com.kks.maingame.cameracontrol");
            DrawCameraPresetBar(new Rect(panel.x + (wrap ? 0 : groupWidth + cameraWidth), y + (wrap ? 24 : 0), subCameraWidth, 23), 1, "サブカメラ", "com.kks.maingame.subcameradisplayprobe");
            DrawActorLocationsBar(new Rect(panel.x + (wrap ? groupWidth : groupWidth + cameraWidth + subCameraWidth), y + (wrap ? 24 : 0), actorWidth, 23));
            if (Time.unscaledTime >= _cameraPresetResolveAt) _cameraPresetResolveAt = Time.unscaledTime + 3;
            GUI.Label(new Rect(x + 2f, y + 2f, 42f, 20f), "ライト");
            x += 44f;
            try
            {
                if (_clubLightsBarRead == null && Time.unscaledTime >= _clubLightsBarNextResolve)
                {
                    _clubLightsBarNextResolve = Time.unscaledTime + 3f;
                    if (Chainloader.PluginInfos.TryGetValue("com.kks.maingame.clublights", out var info))
                    {
                        var type = info.Instance.GetType();
                        var read = type.GetMethod("GetPlaybackBarLightState");
                        var step = type.GetMethod("StepPlaybackBarLightPreset");
                        var gizmoRead = type.GetMethod("GetPlaybackLightGizmos");
                        var gizmoToggle = type.GetMethod("TogglePlaybackLightGizmos");
                        if (gizmoRead != null && gizmoToggle != null) {
                            _lightGizmoRead = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), gizmoRead);
                            _lightGizmoToggle = (Action)Delegate.CreateDelegate(typeof(Action), gizmoToggle);
                        }
                        if (read != null && step != null)
                        {
                            _clubLightsBarRead = (Func<object[]>)Delegate.CreateDelegate(typeof(Func<object[]>), read);
                            _clubLightsBarStep = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), step);
                        }
                    }
                }
                var state = _clubLightsBarRead?.Invoke();
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && _lightGizmoToggle != null;
                bool gizmos = _lightGizmoRead?.Invoke() ?? false;
                if (GUI.Toggle(new Rect(x, y, 48, 21), gizmos, new GUIContent("ギズモ", "ライトを掴んで移動。離すと現在のプリセットへ保存"), GUI.skin.button) != gizmos)
                    _lightGizmoToggle?.Invoke();
                x += 50;
                GUI.enabled = enabled && state != null && (bool)state[0];
                if (GUI.Button(new Rect(x, y, 26f, 21f), new GUIContent("◀", "クラブライトの前の保存済み構成")))
                    _clubLightsBarStep(-1);
                x += 29f;
                if (GUI.Button(new Rect(x, y, 26f, 21f), new GUIContent("▶", "クラブライトの次の保存済み構成")))
                    _clubLightsBarStep(1);
                GUI.enabled = enabled;
                x += 31f;
                string text = state == null ? "接続待ち" : (string)state[3];
                float labelW = Mathf.Max(0f, panel.x + groupWidth - x - 4f);
                GUI.Label(new Rect(x, y + 2f, labelW, 19f), text);
            }
            catch (Exception ex)
            {
                _clubLightsBarRead = null;
                _clubLightsBarStep = null;
                LogWarn("ClubLights bar: " + ex.Message);
            }
        }
    }
}
