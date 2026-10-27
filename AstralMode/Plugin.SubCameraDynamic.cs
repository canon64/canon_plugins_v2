using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        // New independent controller for the real-world sub camera.
        // It replaces the simple waist-centered OrbitCamera while keeping the existing
        // Camera / RenderTexture / NearAvatarDisplay pipeline intact.
        private bool _useDynamicSubCamera = true;
        private bool _dynamicSubCamActive;
        private Vector3 _dynamicSubCamWaist;
        private Vector3 _dynamicSubCamHead;
        private float _dynamicSubCamStartTime;
        private float _nextDynamicSubCamLogTime;

        private void BeginDynamicSubCamera(Vector3 waistWorld, Vector3 headWorld)
        {
            if (!_useDynamicSubCamera)
                return;

            _dynamicSubCamWaist = waistWorld;
            _dynamicSubCamHead = headWorld;
            _dynamicSubCamStartTime = Time.time;
            _dynamicSubCamActive = true;
            _nextDynamicSubCamLogTime = 0f;

            if (_realWorldSubCamOrbit != null)
                _realWorldSubCamOrbit.enabled = false;

            SimpleFileLogger.Info($"DynamicSubCamera begin waist={_dynamicSubCamWaist}, head={_dynamicSubCamHead}, lineLen={Vector3.Distance(_dynamicSubCamWaist, _dynamicSubCamHead):F3}");
        }

        private void UpdateRealWorldSubCameraDynamic()
        {
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut)
            {
                EndDynamicSubCamera();
                return;
            }
            if (!_useDynamicSubCamera) return;
            if (!_dynamicSubCamActive) return;
            if (_realWorldSubCamera == null || _realWorldSubCameraRoot == null) return;
            if (!_realWorldSubCamera.enabled) return;

            float t = Time.time - _dynamicSubCamStartTime;

            // The look-at target moves smoothly along the captured body line.
            // The camera position rotates and bobs around that moving target.
            float focusSpeed = _cfgSubCameraFocusSpeed != null ? _cfgSubCameraFocusSpeed.Value : 0.12f;
            float focusT = Mathf.PingPong(t * focusSpeed, 1f);
            focusT = Mathf.SmoothStep(0f, 1f, focusT);
            Vector3 focus = Vector3.Lerp(_dynamicSubCamWaist, _dynamicSubCamHead, focusT);

            float yawSpeed = _cfgSubCameraYawSpeed != null ? _cfgSubCameraYawSpeed.Value : 10f;
            float pitchBase = _cfgSubCameraPitchBase != null ? _cfgSubCameraPitchBase.Value : 8f;
            float pitchAmp = _cfgSubCameraPitchAmp != null ? _cfgSubCameraPitchAmp.Value : 5f;
            float distanceBase = _cfgSubCameraDistance != null ? _cfgSubCameraDistance.Value : 2.5f;
            float distanceAmp = _cfgSubCameraDistanceAmp != null ? _cfgSubCameraDistanceAmp.Value : 0.25f;
            float bobAmp = _cfgSubCameraBobAmp != null ? _cfgSubCameraBobAmp.Value : 0.35f;

            float yaw = t * yawSpeed;
            float pitch = pitchBase + Mathf.Sin(t * 0.37f) * pitchAmp;
            float distance = distanceBase + Mathf.Sin(t * 0.21f) * distanceAmp;
            float verticalBob = Mathf.Sin(t * 0.35f) * bobAmp;

            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 cameraPosition = focus + orbitRotation * Vector3.back * distance;
            cameraPosition += Vector3.up * verticalBob;

            _realWorldSubCameraRoot.transform.position = cameraPosition;
            _realWorldSubCameraRoot.transform.LookAt(focus, Vector3.up);

            // Keep the near display live-updated so ConfigManager changes apply during AstralMode.
            RepositionDisplays(GetAstralCharaCenter());

            // FOV breathing. Lower FOV feels closer, higher FOV feels farther.
            float fovBase = _cfgSubCameraFovBase != null ? _cfgSubCameraFovBase.Value : 38f;
            float fovAmp = _cfgSubCameraFovAmp != null ? _cfgSubCameraFovAmp.Value : 5f;
            float fovMin = _cfgSubCameraFovMin != null ? _cfgSubCameraFovMin.Value : 24f;
            float fovMax = _cfgSubCameraFovMax != null ? _cfgSubCameraFovMax.Value : 70f;
            if (fovMax < fovMin)
                fovMax = fovMin;

            float fov = fovBase + Mathf.Sin(t * 0.25f) * fovAmp;
            _realWorldSubCamera.fieldOfView = Mathf.Clamp(fov, fovMin, fovMax);

            // [perf] DynamicSubCamera update log removed (astral stutter fix)
        }

        private void EndDynamicSubCamera()
        {
            if (!_dynamicSubCamActive)
                return;

            _dynamicSubCamActive = false;
            SimpleFileLogger.Info("DynamicSubCamera end");
        }
    }
}
