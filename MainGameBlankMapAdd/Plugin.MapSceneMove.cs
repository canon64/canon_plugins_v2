using System;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private string _mapTabMoveStatus;

        private void MoveViewedSceneToActor(ConfigEntry<float>[] entries, bool hotel)
        {
            HSceneProc scene = FindObjectOfType<HSceneProc>();
            CameraControl_Ver2 camera = scene != null && scene.flags != null ? scene.flags.ctrlCamera : null;
            ChaControl actor = FindObjectsOfType<ChaControl>()
                .FirstOrDefault(x => x != null && x.sex == 1);
            if (camera == null || actor == null ||
                (hotel ? _vamHotelPivot == null : _roomOverlayPositionRoot == null))
            {
                _mapTabMoveStatus = "カメラ、キャラ、またはマップを取得できません。";
                return;
            }

            BaseCameraControl_Ver2.CameraData cameraData = camera.GetCameraData();
            Vector3 lookAt = camera.transBase != null
                ? camera.transBase.TransformPoint(cameraData.Pos) : cameraData.Pos;
            Vector3 shift = actor.transform.position - lookAt;
            if (!IsFiniteMapVector(shift))
            {
                _mapTabMoveStatus = "注視点またはキャラ位置が無効です。";
                return;
            }
            if (shift.sqrMagnitude < .000001f)
            {
                _mapTabMoveStatus = "注視点は既にキャラ位置にあります。";
                return;
            }

            Vector3 rootPosition = hotel ? _vamHotelPivot.transform.position
                : _roomOverlayPositionRoot.transform.position;
            Vector3 targetRootPosition = rootPosition + shift;
            Vector3 offset;
            if (hotel)
                offset = targetRootPosition - _vamHotelActorAnchor;
            else
            {
                RoomOverlayPosition settings = EnsureRoomOverlayPositionConfig(_roomOverlayPositionKey);
                Vector3 unrotatedPosition = targetRootPosition;
                if (settings.RotateAroundActor.Value)
                {
                    Quaternion rotation = _roomOverlayBaseRotation * Quaternion.Euler(
                        settings.RotationX.Value, settings.RotationY.Value, settings.RotationZ.Value);
                    Quaternion delta = rotation * Quaternion.Inverse(_roomOverlayBaseRotation);
                    unrotatedPosition = _roomOverlayActorAnchor +
                        Quaternion.Inverse(delta) * (targetRootPosition - _roomOverlayActorAnchor);
                }
                offset = unrotatedPosition - _roomOverlayBasePosition;
            }
            if (!IsFiniteMapVector(offset))
            {
                _mapTabMoveStatus = "移動後のマップ位置が無効です。";
                return;
            }

            cameraData.Pos = camera.transBase != null
                ? camera.transBase.InverseTransformPoint(actor.transform.position)
                : actor.transform.position;
            entries[0].Value = offset.x;
            entries[1].Value = offset.y;
            entries[2].Value = offset.z;
            for (int axis = 0; axis < 3; axis++)
            {
                float coordinate = axis == 0 ? offset.x : axis == 1 ? offset.y : offset.z;
                _mapTabInputs[axis] = coordinate.ToString("R", CultureInfo.InvariantCulture);
                _mapTabSliderCenters[axis] = coordinate;
            }
            if (hotel) ApplyVamHotelTransform();
            else ApplyRoomOverlayPosition();
            camera.SetCameraData(cameraData);
            Config.Save();
            _mapTabMoveStatus = "注視点とマップをキャラ位置へ移動しました。";
        }

        private static bool IsFiniteMapVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }
    }
}
