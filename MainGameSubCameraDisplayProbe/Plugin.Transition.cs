using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private static readonly string[] TransitionEasingLabels = { "Linear", "EaseIn", "EaseOut", "EaseInOut" };

        private bool _transitionActive;
        private float _transitionStartTime;
        private float _transitionDuration;
        private Vector3 _transitionFromPos;
        private Quaternion _transitionFromRot = Quaternion.identity;
        private float _transitionFromFov = 60f;
        private float _transitionCurrentBaseFov = 60f;
        private Vector3 _transitionStaticTargetPos;
        private Quaternion _transitionStaticTargetRot = Quaternion.identity;
        private float _transitionStaticTargetFov = 60f;
        private SubCameraPreset _transitionPreset;
        private bool _transitionDynamicTarget;

        private void StartCameraTransition(SubCameraPreset preset)
        {
            if (preset == null || _cameraAnchorObject == null || _subCamera == null)
                return;

            float duration = Mathf.Clamp(_settings.TransitionSeconds, 0f, 3f);
            float fov = Mathf.Clamp(preset.Fov <= 0f ? _settings.CameraFieldOfView : preset.Fov, 10f, 170f);

            _transitionFromPos = _cameraAnchorObject.transform.position;
            _transitionFromRot = _cameraAnchorObject.transform.rotation;
            // Never feed the beat-zoomed output back into the next transition.
            _transitionFromFov = _transitionActive ? _transitionCurrentBaseFov : _settings.CameraFieldOfView;
            _transitionCurrentBaseFov = _transitionFromFov;

            _transitionPreset = preset;
            _transitionDynamicTarget = preset.UseBoneLink;
            _transitionStaticTargetFov = fov;

            if (preset.UseBoneLink)
            {
                ResolveBoneLinkedTarget(preset, out Vector3 targetPos, out Quaternion targetRot);
                _transitionStaticTargetPos = targetPos;
                _transitionStaticTargetRot = targetRot;
            }
            else
            {
                Vector3 targetPos = ToVector3(preset.CameraPosition, _cameraAnchorObject.transform.position);
                Quaternion targetRot = preset.CameraRotation != null && preset.CameraRotation.Length >= 3
                    ? Quaternion.Euler(ToVector3(preset.CameraRotation, Vector3.zero))
                    : _cameraAnchorObject.transform.rotation;
                _transitionStaticTargetPos = targetPos;
                _transitionStaticTargetRot = targetRot;
            }

            if (duration <= 0.0001f)
            {
                ApplyTransitionFinal(preset);
                return;
            }

            _transitionDuration = duration;
            _transitionStartTime = Time.unscaledTime;
            _transitionActive = true;
            ClearBoneFollow();
            LogInfo("transition start preset=" + (preset.Name ?? "(null)") + " duration=" + duration.ToString("0.##") + " easing=" + TransitionEasingLabels[Mathf.Clamp(_settings.TransitionEasing, 0, 3)] + " bone=" + preset.UseBoneLink);
        }

        private void UpdateCameraTransition()
        {
            if (!_transitionActive || _cameraAnchorObject == null || _subCamera == null)
                return;

            float rawT = Mathf.Clamp01((Time.unscaledTime - _transitionStartTime) / Mathf.Max(0.0001f, _transitionDuration));
            float easedT = EvaluateTransitionEasing(rawT, _settings.TransitionEasing);

            Vector3 targetPos;
            Quaternion targetRot;
            if (_transitionDynamicTarget && _transitionPreset != null)
            {
                ResolveBoneLinkedTarget(_transitionPreset, out targetPos, out targetRot);
            }
            else
            {
                targetPos = _transitionStaticTargetPos;
                targetRot = _transitionStaticTargetRot;
            }

            Vector3 stepPos = Vector3.Lerp(_transitionFromPos, targetPos, easedT);
            Quaternion stepRot = Quaternion.Slerp(_transitionFromRot, targetRot, easedT);
            float stepFov = Mathf.Lerp(_transitionFromFov, _transitionStaticTargetFov, easedT);
            _transitionCurrentBaseFov = stepFov;

            _cameraAnchorObject.transform.SetPositionAndRotation(stepPos, stepRot);
            _subCamera.fieldOfView = stepFov;

            if (rawT >= 1f)
            {
                ApplyTransitionFinal(_transitionPreset);
            }
        }

        private void ApplyTransitionFinal(SubCameraPreset preset)
        {
            _transitionActive = false;
            _transitionDynamicTarget = false;

            if (preset != null && preset.UseBoneLink)
            {
                ActivateBoneFollow(preset);
            }
            else if (preset != null)
            {
                Vector3 targetPos = ToVector3(preset.CameraPosition, _cameraAnchorObject.transform.position);
                Quaternion targetRot = preset.CameraRotation != null && preset.CameraRotation.Length >= 3
                    ? Quaternion.Euler(ToVector3(preset.CameraRotation, Vector3.zero))
                    : _cameraAnchorObject.transform.rotation;
                _cameraAnchorObject.transform.SetPositionAndRotation(targetPos, targetRot);
                _settings.CameraFieldOfView = Mathf.Clamp(preset.Fov <= 0f ? _settings.CameraFieldOfView : preset.Fov, 10f, 170f);
                _subCamera.fieldOfView = _settings.CameraFieldOfView;
                // 通常プリセットを呼んだら保存モードも「通常」に戻す
                _settings.SelectedSaveMode = SaveModeNormal;
            }

            CaptureTransformsToSettings();
            _transitionPreset = null;
            LogInfo("transition end preset=" + (preset?.Name ?? "(null)"));
        }

        private void ResolveBoneLinkedTarget(SubCameraPreset preset, out Vector3 targetPos, out Quaternion targetRot)
        {
            targetPos = _cameraAnchorObject != null ? _cameraAnchorObject.transform.position : Vector3.zero;
            targetRot = _cameraAnchorObject != null ? _cameraAnchorObject.transform.rotation : Quaternion.identity;
            if (preset == null)
                return;

            ChaControl female = ResolveMainFemale();
            Transform[] boneCache = GetFemaleBoneCache(female);
            Transform bone = ResolveBoneTarget(boneCache, preset.BoneTarget, preset.BoneName);
            if (bone == null)
                return;

            Vector3 lookAtOffsetLocal = ToVector3(preset.LookAtOffsetLocal, Vector3.zero);
            Vector3 lookAt = bone.TransformPoint(lookAtOffsetLocal);

            bool hasCameraPosition = HasCameraPosition(preset);
            if (hasCameraPosition)
            {
                Vector3 cameraOffsetLocal = ToVector3(preset.CameraOffsetLocal, Vector3.zero);
                if (cameraOffsetLocal.sqrMagnitude <= 0.000001f && preset.CameraPosition != null && preset.CameraPosition.Length >= 3)
                {
                    cameraOffsetLocal = ToVector3(preset.CameraPosition, Vector3.zero) - bone.position;
                }
                targetPos = bone.position + cameraOffsetLocal;
            }

            Vector3 forward = lookAt - targetPos;
            if (forward.sqrMagnitude > 0.000001f)
                targetRot = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void CancelTransition(string reason)
        {
            if (!_transitionActive)
                return;

            _settings.CameraFieldOfView = _transitionCurrentBaseFov;
            _transitionActive = false;
            _transitionDynamicTarget = false;
            _transitionPreset = null;
            LogInfo("transition canceled reason=" + reason);
        }

        private static float EvaluateTransitionEasing(float t, int easing)
        {
            switch (Mathf.Clamp(easing, 0, 3))
            {
                case 1:
                    return t * t;
                case 2:
                    return 1f - ((1f - t) * (1f - t));
                case 3:
                    return t < 0.5f
                        ? 2f * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                default:
                    return t;
            }
        }
    }
}
