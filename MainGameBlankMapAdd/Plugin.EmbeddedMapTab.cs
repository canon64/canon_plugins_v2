using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private string _mapTabInputKey;
        private bool _mapTabSkyPage;
        private bool _mapTabPendingScrollReset;
        private readonly string[] _mapTabInputs = new string[7];
        private readonly float[] _mapTabSliderCenters = new float[3];
        private readonly float[] _mapTabRotationSliderCenters = new float[3];
        private int _mapTabSliderRangeIndex = 1;
        private int _mapTabRotationRangeIndex = 3;
        private static readonly float[] MapTabSliderRanges = { 1f, 10f, 100f, 1000f, 10000f, 100000f, 1000000f };
        private static readonly float[] MapTabRotationRanges = { 1f, 5f, 15f, 45f, 180f };

        private void DrawEmbeddedMapTab()
        {
            GUILayout.BeginHorizontal();
            Color oldTabColor = GUI.backgroundColor;
            if (!_mapTabSkyPage) GUI.backgroundColor = new Color(.6f, .85f, 1f);
            if (GUILayout.Button("マップ配置", GUILayout.Width(130f)))
            {
                _mapTabSkyPage = false;
                _mapTabPendingScrollReset = true;
            }
            GUI.backgroundColor = _mapTabSkyPage ? new Color(.6f, .85f, 1f) : oldTabColor;
            if (GUILayout.Button("空・環境光", GUILayout.Width(130f)))
            {
                _mapTabSkyPage = true;
                _mapTabPendingScrollReset = true;
            }
            GUI.backgroundColor = oldTabColor;
            GUILayout.EndHorizontal();
            if (!CanUseRoomOverlay())
            {
                GUILayout.Label("オールポーズマップのHシーンで使用できます。");
                return;
            }

            if (_mapTabSkyPage)
            {
                GUILayout.Label("対象マップ: " + (string.IsNullOrEmpty(_roomOverlayDisplayName) ? "元の部屋" : _roomOverlayDisplayName));
                DrawMapSkyControls();
                return;
            }

            GUILayout.Label("追加マップ");

            if (Event.current.type == EventType.Layout) RefreshRoomOverlayChoices();
            GUILayout.BeginHorizontal();
            GUILayout.Label("現在: " + (string.IsNullOrEmpty(_roomOverlayDisplayName) ? "元の部屋" : _roomOverlayDisplayName),
                GUILayout.MinWidth(220f));
            GUI.enabled = !_roomOverlayLoading;
            if (GUILayout.Button("元の部屋に戻す", GUILayout.Width(130f))) ClearRoomOverlay();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (_roomOverlayLoading)
            {
                GUILayout.Label("マップを読み込み中です。");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("街灯のある暗い道路", GUILayout.Width(180f))) SelectStreetlightRoadOverlay();
            if (GUILayout.Button("VaM ホテル（照明あり）", GUILayout.Width(190f))) SelectVamHotelOverlay();
            GUILayout.EndHorizontal();
            if (string.IsNullOrEmpty(_roomOverlaySceneName))
            {
                DrawMapChoiceList();
                return;
            }
            GUILayout.Space(12f);
            GUILayout.Label("選択中のマップの位置と回転（即時反映・マップごとに保存）");
            bool hotel = _roomOverlaySceneName == VamHotelSceneName;
            ConfigEntry<float>[] entries;
            string key;
            if (hotel)
            {
                key = "hotel";
                entries = new[] { _vamHotelOffsetX, _vamHotelOffsetY, _vamHotelOffsetZ,
                    _vamHotelRotationX, _vamHotelRotationY, _vamHotelRotationZ, _vamHotelScale };
            }
            else
            {
                key = _roomOverlayPositionKey;
                if (string.IsNullOrEmpty(key)) return;
                RoomOverlayPosition value = EnsureRoomOverlayPositionConfig(key);
                entries = new[] { value.X, value.Y, value.Z,
                    value.RotationX, value.RotationY, value.RotationZ };
            }
            EnsureMapTabInputs(key, entries);

            GUILayout.BeginHorizontal();
            GUILayout.Label("位置スライダー範囲（設定値を中心）", GUILayout.Width(215f));
            for (int i = 0; i < MapTabSliderRanges.Length; i++)
            {
                Color old = GUI.backgroundColor;
                if (_mapTabSliderRangeIndex == i) GUI.backgroundColor = new Color(.6f, .85f, 1f);
                if (GUILayout.Button("±" + MapTabSliderRanges[i].ToString("0", CultureInfo.InvariantCulture),
                    GUILayout.Width(i < 3 ? 48f : i < 5 ? 63f : 82f)))
                {
                    _mapTabSliderRangeIndex = i;
                    for (int axis = 0; axis < 3; axis++) _mapTabSliderCenters[axis] = entries[axis].Value;
                }
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            for (int axis = 0; axis < 3; axis++)
                DrawMapTabAxis(axis == 0 ? "左右 X" : axis == 1 ? "上下 Y" : "前後 Z",
                    axis, entries[axis], hotel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("回転スライダー範囲（度）", GUILayout.Width(185f));
            for (int i = 0; i < MapTabRotationRanges.Length; i++)
            {
                Color old = GUI.backgroundColor;
                if (_mapTabRotationRangeIndex == i) GUI.backgroundColor = new Color(.6f, .85f, 1f);
                if (GUILayout.Button("±" + MapTabRotationRanges[i].ToString("0", CultureInfo.InvariantCulture),
                    GUILayout.Width(56f)))
                {
                    _mapTabRotationRangeIndex = i;
                    for (int axis = 0; axis < 3; axis++)
                        _mapTabRotationSliderCenters[axis] = entries[axis + 3].Value;
                }
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            for (int axis = 0; axis < 3; axis++)
                DrawMapTabRotationAxis("回転 " + (axis == 0 ? "X" : axis == 1 ? "Y" : "Z"),
                    axis, entries[axis + 3], hotel);
            if (hotel)
                GUILayout.Label("回転中心: キャラ位置（ホテルは固定）");
            else
            {
                RoomOverlayPosition position = EnsureRoomOverlayPositionConfig(key);
                GUILayout.Label("回転中心: " + (position.RotateAroundActor.Value ? "読み込み時のキャラ位置" : "マップの原点"));
                if (GUILayout.Button(position.RotateAroundActor.Value
                    ? "マップ原点を回転中心にする" : "キャラ位置を回転中心にする", GUILayout.Width(225f)))
                    SetMapTabRotationCenter(position, !position.RotateAroundActor.Value);
            }
            if (hotel) DrawMapTabNumber("倍率", 6, entries[6], .05f, 20f);
            if (GUILayout.Button("今見ている場面をキャラ位置へ移動", GUILayout.Width(260f)))
                MoveViewedSceneToActor(entries, hotel);
            if (!string.IsNullOrEmpty(_mapTabMoveStatus)) GUILayout.Label(_mapTabMoveStatus);
            if (GUILayout.Button("位置を初期値へ戻す", GUILayout.Width(180f)))
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    entries[axis].Value = 0f;
                    _mapTabInputs[axis] = "0";
                    _mapTabSliderCenters[axis] = 0f;
                }
                Config.Save();
                ApplyMapTabTransform(hotel);
            }
            if (GUILayout.Button("回転を初期値へ戻す", GUILayout.Width(180f)))
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    entries[axis + 3].Value = 0f;
                    _mapTabInputs[axis + 3] = "0";
                    _mapTabRotationSliderCenters[axis] = 0f;
                }
                Config.Save();
                ApplyMapTabTransform(hotel);
            }
            GUILayout.Space(12f);
            GUILayout.Label("別のマップを呼び出す");
            DrawMapChoiceList();
        }

        private void DrawMapChoiceList()
        {
            for (int i = 0; i < _roomOverlayChoices.Count; i++)
            {
                MapInfo.Param choice = _roomOverlayChoices[i];
                string name = string.IsNullOrEmpty(choice.DisplayName) ? choice.MapName : choice.DisplayName;
                if (GUILayout.Button(name, GUILayout.MaxWidth(400f))) SelectRoomOverlay(choice);
            }
        }

        private void EnsureMapTabInputs(string key, ConfigEntry<float>[] entries)
        {
            if (_mapTabInputKey == key) return;
            _mapTabInputKey = key;
            for (int i = 0; i < entries.Length; i++)
                _mapTabInputs[i] = entries[i].Value.ToString("0.###", CultureInfo.InvariantCulture);
            for (int i = 0; i < 3; i++) _mapTabSliderCenters[i] = entries[i].Value;
            for (int i = 0; i < 3; i++) _mapTabRotationSliderCenters[i] = entries[i + 3].Value;
        }

        private void DrawMapTabAxis(string label, int index, ConfigEntry<float> entry, bool hotel)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));
            string previous = _mapTabInputs[index] ?? string.Empty;
            string input = GUILayout.TextField(previous, GUILayout.Width(125f));
            _mapTabInputs[index] = input;
            float parsed;
            if (input != previous && (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                 float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed) && entry.Value != parsed)
            {
                entry.Value = parsed;
                _mapTabSliderCenters[index] = parsed;
                Config.Save();
                ApplyMapTabTransform(hotel);
            }
            float range = MapTabSliderRanges[_mapTabSliderRangeIndex];
            float next = GUILayout.HorizontalSlider(entry.Value,
                _mapTabSliderCenters[index] - range, _mapTabSliderCenters[index] + range,
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
            if (Mathf.Abs(next - entry.Value) > .0001f)
            {
                entry.Value = next;
                _mapTabInputs[index] = next.ToString("R", CultureInfo.InvariantCulture);
                Config.Save();
                ApplyMapTabTransform(hotel);
            }
            if (GUILayout.Button("中心を設定値へ", GUILayout.Width(120f)))
                _mapTabSliderCenters[index] = entry.Value;
            GUILayout.EndHorizontal();
        }

        private void DrawMapTabRotationAxis(string label, int axis, ConfigEntry<float> entry, bool hotel)
        {
            int index = axis + 3;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));
            string previous = _mapTabInputs[index] ?? string.Empty;
            string input = GUILayout.TextField(previous, GUILayout.Width(125f));
            _mapTabInputs[index] = input;
            float parsed;
            if (input != previous && (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed) && entry.Value != parsed)
            {
                entry.Value = parsed;
                _mapTabRotationSliderCenters[axis] = parsed;
                Config.Save();
                ApplyMapTabTransform(hotel);
            }
            float range = MapTabRotationRanges[_mapTabRotationRangeIndex];
            float next = GUILayout.HorizontalSlider(entry.Value,
                _mapTabRotationSliderCenters[axis] - range, _mapTabRotationSliderCenters[axis] + range,
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
            if (Mathf.Abs(next - entry.Value) > .0001f)
            {
                entry.Value = next;
                _mapTabInputs[index] = next.ToString("R", CultureInfo.InvariantCulture);
                Config.Save();
                ApplyMapTabTransform(hotel);
            }
            if (GUILayout.Button("中心を設定値へ", GUILayout.Width(120f)))
                _mapTabRotationSliderCenters[axis] = entry.Value;
            GUILayout.EndHorizontal();
        }

        private void DrawMapTabNumber(string label, int index, ConfigEntry<float> entry, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));
            string previous = _mapTabInputs[index] ?? string.Empty;
            string input = GUILayout.TextField(previous, GUILayout.Width(125f));
            _mapTabInputs[index] = input;
            float parsed;
            if (input != previous && (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                 float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed))
            {
                float value = Mathf.Clamp(parsed, min, max);
                if (entry.Value != value)
                {
                    entry.Value = value;
                    Config.Save();
                    ApplyVamHotelTransform();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyMapTabTransform(bool hotel)
        {
            if (hotel) ApplyVamHotelTransform();
            else
            {
                ApplyRoomOverlayPosition();
                if (_roomOverlaySceneName == StreetlightRoadSceneKey) ApplyStreetlightCameraPreview();
            }
        }

        private void SetMapTabRotationCenter(RoomOverlayPosition value, bool useActor)
        {
            if (_roomOverlayPositionRoot == null) return;
            Vector3 oldPosition = _roomOverlayPositionRoot.transform.position;
            Quaternion rotation = _roomOverlayBaseRotation * Quaternion.Euler(
                value.RotationX.Value, value.RotationY.Value, value.RotationZ.Value);
            Quaternion delta = rotation * Quaternion.Inverse(_roomOverlayBaseRotation);
            Vector3 unrotatedPosition = useActor
                ? _roomOverlayActorAnchor + Quaternion.Inverse(delta) * (oldPosition - _roomOverlayActorAnchor)
                : oldPosition;
            Vector3 offset = unrotatedPosition - _roomOverlayBasePosition;
            value.X.Value = offset.x;
            value.Y.Value = offset.y;
            value.Z.Value = offset.z;
            for (int i = 0; i < 3; i++)
            {
                float coordinate = i == 0 ? offset.x : i == 1 ? offset.y : offset.z;
                _mapTabInputs[i] = coordinate.ToString("R", CultureInfo.InvariantCulture);
                _mapTabSliderCenters[i] = coordinate;
            }
            value.RotateAroundActor.Value = useActor;
            Config.Save();
            ApplyMapTabTransform(false);
        }
    }
}
