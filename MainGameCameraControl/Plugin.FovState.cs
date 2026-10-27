using UnityEngine;

namespace MainGameCameraControl
{
    internal sealed partial class Plugin
    {
        // _lastBaseFov is the single unzoomed value. CameraData always uses this
        // domain; only Unity Camera.fieldOfView receives the visual beat effect.
        private float _fovResetBase = 35f;
        private bool _transitionManualFov;
        private float _transitionManualBaseFov;
        private float _externalDisplayFov;
        private string _externalFovReason = string.Empty;

        private void SetBaseFov(float value)
        {
            float clamped = Mathf.Clamp(value <= 0f ? Settings.DefaultFov : value, 5f, 120f);
            _lastBaseFov = clamped;
            _beatFovBaseFov = clamped;
            _currentData.Fov = clamped;
            if (_boneLinkActive) _activeBoneLinkFov = clamped;
            if (!_transitionActive) _targetData.Fov = clamped;
        }

        private void ApplyManualBaseFov(float value, bool activateBeat = true)
        {
            SetBaseFov(value);
            if (activateBeat) _beatFovSuspendedForNewController = false;
            if (_transitionActive)
            {
                _transitionManualFov = true;
                _transitionManualBaseFov = _lastBaseFov;
                _transitionFrom.Fov = _lastBaseFov;
                _transitionTo.Fov = _lastBaseFov;
                if (_transitionState != null) _transitionState.From.Fov = _lastBaseFov;
            }
            _targetData.Fov = _lastBaseFov;
            if (_ctrlCamera != null) _ctrlCamera.CameraFov = _lastBaseFov;
            RenderCurrentFov("manual-base");
        }

        private float ResolveDisplayFov()
        {
            return IsExternalFovHoldActive() && _externalDisplayFov > 0f
                ? _externalDisplayFov
                : (_beatFovSuspendedForNewController ? ResolveBeatBaseFov() : ResolveBeatZoomedFov(ResolveBeatBaseFov()));
        }

        private void RenderCurrentFov(string reason)
        {
            if (_ctrlCamera == null || IsVrFovBlocked()) return;
            float display = ResolveDisplayFov();
            _lastAppliedFov = display;
            if (CanWriteFov(_ctrlCamera.thisCmaera)
                && _ctrlCamera.thisCmaera.fieldOfView != display)
                _ctrlCamera.thisCmaera.fieldOfView = display;
            ApplyFovToScreenCameras(display, reason, false);
        }

        private bool AcceptsNativeFovInput(BaseCameraControl_Ver2 controller)
        {
            return controller == _ctrlCamera && BlankRoomFovInputAllowed()
                && !IsVrFovBlocked() && !IsExternalFovHoldActive()
                && (IsNativeFovKeyActive() || Input.GetKeyDown(KeyCode.R));
        }

        private void PrepareNativeFovInput()
        {
            // Keyboard adjustment starts from the rendered view, not a cached
            // preset/controller value that can differ after changing ownership.
            float displayed = ResolveLiveCameraFov(_ctrlCamera.CameraFov);
            float currentBase = displayed;
            if (IsBeatFovEnabled() && !_beatFovSuspendedForNewController)
            {
                // Undo only the effect already rendered. Sampling the next beat
                // here would turn animation progress into a permanent FOV edit.
                float renderedMultiplier = _lastAppliedFov > 0f && _lastBaseFov > 0f
                    ? _lastBaseFov / _lastAppliedFov : 1f;
                currentBase = displayed * Mathf.Max(1f, renderedMultiplier);
            }
            SetBaseFov(currentBase);
            // CameraInitFov also changes CameraFov, so restore the current base
            // after setting the unzoomed reset value, before the native method.
            _ctrlCamera.CameraInitFov = _fovResetBase;
            _ctrlCamera.CameraFov = _lastBaseFov;
        }

        private void AcceptExternalDisplayFov(float value, string reason)
        {
            _externalDisplayFov = Mathf.Clamp(value, 5f, 179f);
            _externalFovReason = reason ?? string.Empty;
            _externalFovHoldUntil = Time.unscaledTime + 1f;
            RenderCurrentFov(reason);
        }
    }
}
