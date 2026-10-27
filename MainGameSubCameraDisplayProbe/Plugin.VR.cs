using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private Controller _heldController;
        private Controller.Lock _heldControllerFocusLock = Controller.Lock.Invalid;
        private GameObject _heldObject;
        private Vector3 _holdLocalPosition;
        private Quaternion _holdLocalRotation = Quaternion.identity;
        private bool _vrCaptureTriggerHeld;
        private bool _vrCaptureTriggerConsumed;
        private bool _vrVideoStartedByTriggerHold;
        private Controller _vrCaptureTriggerController;
        private float _vrCaptureTriggerDownAt;
        private Controller _transparentController;
        private readonly List<ControllerRendererState> _transparentControllerRendererStates = new List<ControllerRendererState>();
        private const float CameraAttachedPreviewHeight = 0.23f;
        private static readonly Quaternion CameraAttachedPreviewLocalRotation = Quaternion.identity;

        private struct ControllerRendererState
        {
            public Renderer Renderer;
            public bool Enabled;
        }

        private bool IsHoldingCameraAnchor()
        {
            return _heldObject != null
                && _cameraAnchorObject != null
                && ReferenceEquals(_heldObject, _cameraAnchorObject);
        }

        private bool IsHoldingBoneLinkedCamera()
        {
            return IsHoldingCameraAnchor() && _boneFollowActive;
        }

        private void UpdateVrGrab()
        {
            if (!_settings.EnableVrGrip || !VR.Active || VR.Mode == null)
            {
                UpdateHandPreview(null);
                if (_heldController != null || (_heldControllerFocusLock != null && _heldControllerFocusLock.IsValid))
                    StopGrip();
                return;
            }

            if (_heldController == null)
            {
                if (_settings.GripMovesCamera && TryStartGrip(VR.Mode.Left, _cameraAnchorObject))
                    return;
                if (_settings.GripMovesCamera && TryStartGrip(VR.Mode.Right, _cameraAnchorObject))
                    return;
                if (_settings.GripMovesDisplay && TryStartGrip(VR.Mode.Left, _displayAnchorObject))
                    return;
                if (_settings.GripMovesDisplay)
                    TryStartGrip(VR.Mode.Right, _displayAnchorObject);
                return;
            }

            if (_heldController.Input == null || _heldObject == null)
            {
                UpdateHandPreview(null);
                StopGrip();
                return;
            }

            if (!Bindings.VrHeld("MainGameSubCameraDisplayProbe.grab", _heldController.Input, "Grip"))
            {
                UpdateHandPreview(null);
                StopGrip(true);
                return;
            }

            Transform hand = ((Component)_heldController).transform;
            _heldObject.transform.position = hand.position + hand.rotation * _holdLocalPosition;
            _heldObject.transform.rotation = hand.rotation * _holdLocalRotation;
            UpdateHandPreview(hand);
            UpdateHeldCameraFovFromStick(_heldController);
            UpdateHeldCameraCaptureTrigger(_heldController);
            if (_actorLease != 0 && IsHoldingCameraAnchor())
            {
                SyncExternalCameraPositionDisplay();
            }
            else if (IsHoldingBoneLinkedCamera())
            {
                CaptureActiveBoneCameraOffset();
                ApplyHeldBoneCameraLookAtRotation();
            }
            else
            {
                PersistTransformsToSettings();
            }
        }

        private void ApplyHeldBoneCameraLookAtRotation()
        {
            if (!IsHoldingBoneLinkedCamera())
                return;

            ApplyBoneLookAtRotationOnly();
        }

        private bool TryStartGrip(Controller controller, GameObject target)
        {
            if (controller == null || controller.Input == null || target == null)
                return false;

            if (!Bindings.VrDown("MainGameSubCameraDisplayProbe.grab", controller.Input, "Grip"))
                return false;

            Transform hand = ((Component)controller).transform;
            if (Vector3.Distance(hand.position, target.transform.position) > _settings.VrGripStartDistance)
                return false;

            if (!controller.TryAcquireFocus(out Controller.Lock focusLock) || focusLock == null || !focusLock.IsValid)
            {
                LogWarn("vr grip rejected: controller focus acquire failed controller=" + controller.name);
                return false;
            }

            _heldController = controller;
            _heldControllerFocusLock = focusLock;
            _heldObject = target;
            _holdLocalPosition = Quaternion.Inverse(hand.rotation) * (target.transform.position - hand.position);
            _holdLocalRotation = Quaternion.Inverse(hand.rotation) * target.transform.rotation;
            UpdateHeldControllerVisibility();
            UpdateHandPreview(hand);
            LogInfo("vr grip start target=" + target.name + " controller=" + controller.name);
            return true;
        }

        private void StopGrip(bool savePlacement = false)
        {
            bool heldCameraAnchor = IsHoldingCameraAnchor();
            CancelHeldCameraCaptureTrigger("grip-end");
            UpdateHandPreview(null);
            if (_heldObject != null)
                LogInfo("vr grip end target=" + _heldObject.name);
            if (heldCameraAnchor && savePlacement)
            {
                SaveMovedCameraPreset();
            }
            if (_heldObject != null && savePlacement) PersistTransformsToSettings();
            RestoreTransparentController();
            ReleaseHeldControllerFocus();
            _heldController = null;
            _heldObject = null;
        }

        private void UpdateHandPreview(Transform hand)
        {
            if (_handPreviewObject == null)
                return;

            bool shouldShow =
                hand != null &&
                _renderTexture != null &&
                _settings.ShowHandPreviewWhileGrabbingCamera &&
                _heldObject == _cameraAnchorObject &&
                _cameraAnchorObject != null;

            if (!shouldShow)
            {
                if (_handPreviewObject.activeSelf)
                {
                    _handPreviewObject.SetActive(false);
                    LogInfo("hand preview hide");
                }
                return;
            }

            if (!_handPreviewObject.activeSelf)
            {
                _handPreviewObject.SetActive(true);
                LogInfo("hand preview show hand=" + hand.name);
            }

            Transform cameraTransform = _cameraAnchorObject.transform;
            if (_handPreviewObject.transform.parent != cameraTransform)
                _handPreviewObject.transform.SetParent(cameraTransform, false);

            float previewHeight = CameraAttachedPreviewHeight;
            float previewWidth = previewHeight * ResolveRenderTextureAspect();
            _handPreviewObject.transform.localPosition = new Vector3(
                _settings.HandPreviewOffsetX,
                _settings.HandPreviewOffsetY,
                _settings.HandPreviewOffsetZ);
            _handPreviewObject.transform.localRotation = CameraAttachedPreviewLocalRotation;
            _handPreviewObject.transform.localScale = new Vector3(previewWidth, previewHeight, 1f);
        }

        private float ResolveRenderTextureAspect()
        {
            if (_renderTexture != null && _renderTexture.width > 0 && _renderTexture.height > 0)
                return Mathf.Clamp(_renderTexture.width / (float)_renderTexture.height, 0.1f, 10f);

            int width = _settings != null ? Mathf.Max(1, _settings.RenderWidth) : 16;
            int height = _settings != null ? Mathf.Max(1, _settings.RenderHeight) : 9;
            return Mathf.Clamp(width / (float)height, 0.1f, 10f);
        }

        private void UpdateHeldControllerVisibility()
        {
            if (_heldController != null && _heldObject == _cameraAnchorObject)
            {
                MakeControllerTransparent(_heldController);
                return;
            }

            RestoreTransparentController();
        }

        private void MakeControllerTransparent(Controller controller)
        {
            if (controller == null || _transparentController == controller)
                return;

            RestoreTransparentController();

            Component component = controller as Component;
            if (component == null)
                return;

            Renderer[] renderers = component.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                _transparentControllerRendererStates.Add(new ControllerRendererState
                {
                    Renderer = renderer,
                    Enabled = renderer.enabled
                });
                renderer.enabled = false;
            }

            _transparentController = controller;
            LogInfo("vr controller transparent controller=" + controller.name + " renderers=" + _transparentControllerRendererStates.Count);
        }

        private void RestoreTransparentController()
        {
            if (_transparentController == null && _transparentControllerRendererStates.Count == 0)
                return;

            for (int i = 0; i < _transparentControllerRendererStates.Count; i++)
            {
                ControllerRendererState state = _transparentControllerRendererStates[i];
                if (state.Renderer != null)
                    state.Renderer.enabled = state.Enabled;
            }

            string controllerName = _transparentController != null ? _transparentController.name : "(null)";
            _transparentControllerRendererStates.Clear();
            _transparentController = null;
            LogInfo("vr controller restore controller=" + controllerName);
        }

        private void UpdateHeldCameraFovFromStick(Controller controller)
        {
            if (!_settings.VrFovStickEnabled || _heldObject != _cameraAnchorObject || controller == null || controller.Input == null)
                return;

            Vector2 axis = controller.Input.GetAxis();
            float deadzone = Mathf.Clamp(_settings.VrFovStickDeadzone, 0f, 0.95f);
            float y = Bindings.Stick("MainGameSubCameraDisplayProbe.fovStick", axis);
            if (Mathf.Abs(y) <= deadzone)
                return;

            float normalized = Mathf.Sign(y) * ((Mathf.Abs(y) - deadzone) / Mathf.Max(0.01f, 1f - deadzone));
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float speed = Mathf.Clamp(_settings.VrFovStickSpeed, 1f, 120f);
            float nextFov = Round2(Mathf.Clamp(_settings.CameraFieldOfView + normalized * speed * deltaTime, 10f, 170f));
            if (Mathf.Abs(nextFov - _settings.CameraFieldOfView) <= 0.001f)
                return;

            _settings.CameraFieldOfView = nextFov;
            if (_subCamera != null)
                _subCamera.fieldOfView = nextFov;
            SyncActiveFovFromUi();
            SetStatus("FOV: " + nextFov.ToString("0.##"));
        }

        private void UpdateHeldCameraCaptureTrigger(Controller controller)
        {
            if (_heldObject != _cameraAnchorObject || controller == null || controller.Input == null)
            {
                CancelHeldCameraCaptureTrigger("target-lost");
                return;
            }

            if (!_vrCaptureTriggerHeld)
            {
                if (!IsCaptureTriggerDown(controller))
                    return;

                _vrCaptureTriggerHeld = true;
                _vrCaptureTriggerConsumed = false;
                _vrVideoStartedByTriggerHold = false;
                _vrCaptureTriggerController = controller;
                _vrCaptureTriggerDownAt = Time.unscaledTime;
                LogInfo("vr capture trigger down controller=" + controller.name);
                return;
            }

            if (_vrCaptureTriggerController != controller || !IsCaptureTriggerHeld(controller))
            {
                FinishHeldCameraCaptureTrigger("trigger-release");
                return;
            }

            float holdSeconds = Mathf.Clamp(_settings.VideoTriggerHoldSeconds, 0.1f, 5f);
            if (_vrCaptureTriggerConsumed || Time.unscaledTime - _vrCaptureTriggerDownAt < holdSeconds)
                return;

            _vrCaptureTriggerConsumed = true;
            if (_videoRecording)
            {
                LogInfo("vr long trigger ignored: video already recording");
                SetStatus("録画中");
                return;
            }

            LogInfo("vr long trigger video start controller=" + controller.name + " holdSeconds=" + holdSeconds.ToString("0.##"));
            if (StartVideoRecording())
                _vrVideoStartedByTriggerHold = true;
        }

        private void FinishHeldCameraCaptureTrigger(string reason)
        {
            if (!_vrCaptureTriggerHeld)
                return;

            if (_vrVideoStartedByTriggerHold)
            {
                StopVideoRecording("vr-trigger-release");
            }
            else if (!_vrCaptureTriggerConsumed)
            {
                LogInfo("photo trigger release enabled=" + _settings.PhotoCaptureOnTrigger + " reason=" + reason);
                if (_settings.PhotoCaptureOnTrigger)
                    CaptureSubCameraPhoto("vr-trigger");
                else
                    SetStatus("写真保存OFF");
            }

            ResetHeldCameraCaptureTriggerState();
        }

        private void CancelHeldCameraCaptureTrigger(string reason)
        {
            if (_vrVideoStartedByTriggerHold)
                StopVideoRecording("vr-trigger-" + reason);
            ResetHeldCameraCaptureTriggerState();
        }

        private void ResetHeldCameraCaptureTriggerState()
        {
            _vrCaptureTriggerHeld = false;
            _vrCaptureTriggerConsumed = false;
            _vrVideoStartedByTriggerHold = false;
            _vrCaptureTriggerController = null;
            _vrCaptureTriggerDownAt = 0f;
        }

        private static bool IsCaptureTriggerDown(Controller controller)
        {
            if (controller == null || controller.Input == null)
                return false;

            return Bindings.VrDown("MainGameSubCameraDisplayProbe.capture", controller.Input, "Trigger");
        }

        private static bool IsCaptureTriggerHeld(Controller controller)
        {
            if (controller == null || controller.Input == null)
                return false;

            return Bindings.VrHeld("MainGameSubCameraDisplayProbe.capture", controller.Input, "Trigger");
        }

        private void ReleaseHeldControllerFocus()
        {
            if (_heldControllerFocusLock == null || !_heldControllerFocusLock.IsValid)
            {
                _heldControllerFocusLock = Controller.Lock.Invalid;
                return;
            }

            try
            {
                _heldControllerFocusLock.SafeRelease();
            }
            catch (Exception ex)
            {
                LogWarn("vr grip focus release failed: " + ex.Message);
            }

            _heldControllerFocusLock = Controller.Lock.Invalid;
        }
    }
}
