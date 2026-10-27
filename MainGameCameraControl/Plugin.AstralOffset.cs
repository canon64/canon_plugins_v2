using System;
using UnityEngine;

namespace MainGameCameraControl
{
    internal sealed partial class Plugin
    {
        /// <summary>
        /// Shifts the active CameraControl_Ver2 data by a world-space offset.
        /// This is intended for external world-relocation systems such as AstralMode.
        /// Only CameraData.Pos is shifted; Dir/Rot/Fov remain relative/visual parameters.
        /// Calling this with the negative offset restores the camera control basis.
        /// </summary>
        public static bool TryApplyAstralWorldOffset(Vector3 offset)
        {
            Plugin inst = Instance;
            if (inst == null)
                return false;

            return inst.ApplyAstralWorldOffsetInternal(offset);
        }

        private bool ApplyAstralWorldOffsetInternal(Vector3 offset)
        {
            RefreshCameraControllerCache();
            if (!_hasCameraController || _ctrlCamera == null)
            {
                LogWarn("[astral-offset] skipped: CameraControl_Ver2 not found offset=" + FormatVectorValue(offset));
                return false;
            }

            try
            {
                BaseCameraControl_Ver2.CameraData live = _ctrlCamera.GetCameraData();
                live = OffsetCameraDataPosition(live, offset);
                _currentData = OffsetCameraDataPosition(_currentData, offset);
                _targetData = OffsetCameraDataPosition(_targetData, offset);

                if (_transitionActive)
                {
                    _transitionFrom = OffsetCameraDataPosition(_transitionFrom, offset);
                    _transitionTo = OffsetCameraDataPosition(_transitionTo, offset);
                }

                if (_transitionState != null)
                {
                    _transitionState.From = OffsetCameraDataPosition(_transitionState.From, offset);
                    _transitionState.InitialTarget = OffsetCameraDataPosition(_transitionState.InitialTarget, offset);
                }

                if (_lookAtAnchor != null)
                    _lookAtAnchor.position += offset;

                ApplyCameraData(live, "astral-offset");
                _currentData = live;
                if (!_transitionActive)
                    _targetData = live;

                LogInfo(
                    "[astral-offset] applied offset=" + FormatVectorValue(offset)
                    + " livePos=" + FormatVectorValue(live.Pos)
                    + " transitionActive=" + _transitionActive
                    + " boneLink=" + _boneLinkActive);
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("[astral-offset] failed offset=" + FormatVectorValue(offset) + " error=" + ex.Message);
                return false;
            }
        }

        private static BaseCameraControl_Ver2.CameraData OffsetCameraDataPosition(BaseCameraControl_Ver2.CameraData data, Vector3 offset)
        {
            data.Pos += offset;
            return data;
        }

        private static string FormatVectorValue(Vector3 value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.###},{1:0.###},{2:0.###})",
                value.x,
                value.y,
                value.z);
        }
    }
}
