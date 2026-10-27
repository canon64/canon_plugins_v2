using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private sealed class RoomOverlayPosition
        {
            internal ConfigEntry<float> X;
            internal ConfigEntry<float> Y;
            internal ConfigEntry<float> Z;
            internal ConfigEntry<float> RotationX;
            internal ConfigEntry<float> RotationY;
            internal ConfigEntry<float> RotationZ;
            internal ConfigEntry<bool> RotateAroundActor;
        }

        private readonly Dictionary<string, RoomOverlayPosition> _roomOverlayPositions =
            new Dictionary<string, RoomOverlayPosition>();
        private readonly string[] _roomOverlayPositionInputs = new string[3];

        private RoomOverlayPosition EnsureRoomOverlayPositionConfig(string key)
        {
            RoomOverlayPosition position;
            if (_roomOverlayPositions.TryGetValue(key, out position)) return position;
            string section = "11.RoomOverlayPosition." + key;
            position = new RoomOverlayPosition
            {
                X = Config.Bind(section, "PositionX", 0f, "自動配置位置からの左右オフセット"),
                Y = Config.Bind(section, "PositionY", 0f, "自動配置位置からの上下オフセット"),
                Z = Config.Bind(section, "PositionZ", 0f, "自動配置位置からの前後オフセット"),
                RotationX = Config.Bind(section, "RotationX", 0f, "マップのX軸回転角度"),
                RotationY = Config.Bind(section, "RotationY", 0f, "マップのY軸回転角度"),
                RotationZ = Config.Bind(section, "RotationZ", 0f, "マップのZ軸回転角度"),
                RotateAroundActor = Config.Bind(section, "RotateAroundActor", false,
                    "true: 読み込み時のキャラ位置を中心にマップを回転")
            };
            _roomOverlayPositions.Add(key, position);
            return position;
        }

        private void ApplyRoomOverlayPosition()
        {
            if (_roomOverlayPositionRoot == null || string.IsNullOrEmpty(_roomOverlayPositionKey)) return;
            RoomOverlayPosition value = EnsureRoomOverlayPositionConfig(_roomOverlayPositionKey);
            Quaternion rotation = _roomOverlayBaseRotation *
                Quaternion.Euler(value.RotationX.Value, value.RotationY.Value, value.RotationZ.Value);
            Vector3 position = _roomOverlayBasePosition + new Vector3(value.X.Value, value.Y.Value, value.Z.Value);
            if (value.RotateAroundActor.Value)
            {
                Quaternion delta = rotation * Quaternion.Inverse(_roomOverlayBaseRotation);
                position = _roomOverlayActorAnchor + delta * (position - _roomOverlayActorAnchor);
            }
            _roomOverlayPositionRoot.transform.SetPositionAndRotation(position, rotation);
        }

        private void ResetRoomOverlayPositionInputs()
        {
            if (string.IsNullOrEmpty(_roomOverlayPositionKey)) return;
            RoomOverlayPosition value = EnsureRoomOverlayPositionConfig(_roomOverlayPositionKey);
            _roomOverlayPositionInputs[0] = value.X.Value.ToString("0.###", CultureInfo.InvariantCulture);
            _roomOverlayPositionInputs[1] = value.Y.Value.ToString("0.###", CultureInfo.InvariantCulture);
            _roomOverlayPositionInputs[2] = value.Z.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void DrawRoomOverlayPositionPopup()
        {
            if (string.IsNullOrEmpty(_roomOverlayPositionKey)) { _roomOverlayPositionEditOpen = false; return; }
            RoomOverlayPosition value = EnsureRoomOverlayPositionConfig(_roomOverlayPositionKey);
            const float width = 330f;
            const float height = 160f;
            _roomOverlayListRect = new Rect(_roomOverlayButtonRect.x,
                Mathf.Max(2f, _roomOverlayButtonRect.y - height - 3f), width, height);
            GUI.Box(_roomOverlayListRect, "マップ位置調整（変更は即時反映・自動保存）");
            float x = _roomOverlayListRect.x + 9f;
            float y = _roomOverlayListRect.y + 27f;
            DrawRoomOverlayPositionRow(x, y, "左右 X", 0, value.X);
            DrawRoomOverlayPositionRow(x, y + 30f, "上下 Y", 1, value.Y);
            DrawRoomOverlayPositionRow(x, y + 60f, "前後 Z", 2, value.Z);
            if (GUI.Button(new Rect(x, y + 95f, 145f, 25f), "位置を初期値へ戻す"))
            {
                value.X.Value = value.Y.Value = value.Z.Value = 0f;
                Config.Save();
                ResetRoomOverlayPositionInputs();
                ApplyRoomOverlayPosition();
            }
            if (GUI.Button(new Rect(x + 194f, y + 95f, 115f, 25f), "閉じる"))
            {
                _roomOverlayPositionEditOpen = false;
                _roomOverlayDropdownOpen = false;
            }
        }

        private void DrawRoomOverlayPositionRow(float x, float y, string label, int index,
            ConfigEntry<float> entry)
        {
            GUI.Label(new Rect(x, y, 95f, 24f), label);
            string input = GUI.TextField(new Rect(x + 105f, y, 180f, 24f),
                _roomOverlayPositionInputs[index] ?? string.Empty);
            _roomOverlayPositionInputs[index] = input;
            float parsed;
            if ((float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                 float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed))
            {
                float clamped = Mathf.Clamp(parsed, -1000f, 1000f);
                if (entry.Value != clamped)
                {
                    entry.Value = clamped;
                    Config.Save();
                }
                ApplyRoomOverlayPosition();
            }
        }
    }
}
