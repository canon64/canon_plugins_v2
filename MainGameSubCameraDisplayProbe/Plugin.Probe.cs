using MainGameTransformGizmo;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private GameObject _layerSyncedDisplayObject, _layerSyncedPreviewObject;
        private int _layerSyncedValue = -1, _layerSyncedDisplayChildren = -1, _layerSyncedPreviewChildren = -1;
        private float _nextDisplayLayerRefreshTime;

        private void EnsureProbe()
        {
            EnsureRoot();
            if (_rootObject == null)
                return;
            EnsureCamera();
            EnsureDisplay();
            ApplyCameraSettings();
            ApplyDisplaySettings();
        }

        private void EnsureCameraProbe()
        {
            EnsureRoot();
            if (_rootObject == null)
                return;
            EnsureCamera();
            ApplyCameraSettings();
        }

        private void EnsureRoot()
        {
            if (_rootObject != null)
                return;

            Camera worldCamera = ResolveWorldCamera();
            if (worldCamera == null)
            {
                LogWarn("probe create skipped: world camera not found");
                return;
            }

            _rootObject = new GameObject("__MainGameSubCameraDisplayProbeRoot");
            MoveProbeInFrontOfPlayer(initialCreate: true);
            LogInfo("probe root created");
        }

        private void EnsureCamera()
        {
            if (_rootObject == null)
                EnsureRoot();
            if (_rootObject == null || _cameraAnchorObject != null)
                return;

            _cameraAnchorObject = new GameObject("SubCameraAnchor");
            _cameraAnchorObject.transform.SetParent(_rootObject.transform, false);
            _cameraAnchorObject.transform.localPosition = new Vector3(_settings.CameraPosX, _settings.CameraPosY, _settings.CameraPosZ);
            _cameraAnchorObject.transform.localRotation = Quaternion.Euler(_settings.CameraRotX, _settings.CameraRotY, _settings.CameraRotZ);

            _cameraVisualObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cameraVisualObject.name = "CameraMarker";
            DestroyImmediate(_cameraVisualObject.GetComponent<Collider>());
            _cameraVisualObject.transform.SetParent(_cameraAnchorObject.transform, false);
            _cameraVisualObject.transform.localPosition = Vector3.zero;
            _cameraVisualObject.transform.localRotation = Quaternion.identity;

            _cameraLensObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cameraLensObject.name = "CameraLens";
            DestroyImmediate(_cameraLensObject.GetComponent<Collider>());
            _cameraLensObject.transform.SetParent(_cameraAnchorObject.transform, false);

            ApplyCameraVisualScale();

            GameObject subCameraObject = new GameObject("SubCamera");
            subCameraObject.transform.SetParent(_cameraAnchorObject.transform, false);
            subCameraObject.transform.localPosition = Vector3.zero;
            subCameraObject.transform.localRotation = Quaternion.identity;
            _subCamera = subCameraObject.AddComponent<Camera>();
            _subCamera.clearFlags = CameraClearFlags.SolidColor;
            _subCamera.backgroundColor = Color.black;

            _renderTexture = CreateRenderTexture();
            _subCamera.targetTexture = _renderTexture;
            _subCamera.allowHDR = false;
            _subCamera.allowMSAA = false;

            _handPreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _handPreviewObject.name = "HandPreviewQuad";
            DestroyImmediate(_handPreviewObject.GetComponent<Collider>());
            _handPreviewMaterial = CreateDisplayMaterial(backFace: false);
            _handPreviewMaterial.mainTexture = _renderTexture;
            _handPreviewObject.GetComponent<Renderer>().material = _handPreviewMaterial;
            SetLayerRecursive(_handPreviewObject, Mathf.Clamp(_settings.DisplayLayer, 0, 30));
            _handPreviewObject.SetActive(false);

            // ディスプレイがすでにあれば、その mainTexture を新しい RenderTexture に更新
            if (_displayMaterial != null)
                _displayMaterial.mainTexture = _renderTexture;

            AttachCameraGizmo();
            ApplyCameraSettings();
            LogInfo("camera created");
        }

        private void EnsureDisplay()
        {
            if (_rootObject == null)
                EnsureRoot();
            if (_rootObject == null || _displayAnchorObject != null)
                return;

            _displayAnchorObject = new GameObject("DisplayAnchor");
            _displayAnchorObject.transform.SetParent(_rootObject.transform, false);
            _displayAnchorObject.transform.localPosition = new Vector3(_settings.DisplayPosX, _settings.DisplayPosY, _settings.DisplayPosZ);
            _displayAnchorObject.transform.localRotation = Quaternion.Euler(_settings.DisplayRotX, _settings.DisplayRotY, _settings.DisplayRotZ);

            _displayObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _displayObject.name = "DisplayQuad";
            _displayObject.transform.SetParent(_displayAnchorObject.transform, false);
            _displayObject.transform.localPosition = Vector3.zero;
            _displayObject.transform.localRotation = Quaternion.identity;
            _displayMaterial = CreateDisplayMaterial(backFace: false);
            _displayMaterial.mainTexture = _renderTexture;
            _displayObject.GetComponent<Renderer>().material = _displayMaterial;

            EnsureDisplayOverlayCamera();
            ApplyDisplayLayer();
            AttachDisplayGizmo();
            ApplyDisplaySettings();
            LogInfo("display created");
        }

        private void ApplyCameraVisualScale()
        {
            if (_settings == null) return;
            float scale = Mathf.Clamp(_settings.CameraVisualScale, 0.1f, 3f);

            if (_cameraVisualObject != null)
            {
                Vector3 visualScale = new Vector3(0.1f, 0.06f, 0.16f) * scale;
                if (_cameraVisualObject.transform.localScale != visualScale)
                    _cameraVisualObject.transform.localScale = visualScale;
            }

            if (_cameraLensObject != null)
            {
                Vector3 lensPosition = new Vector3(0f, 0f, -0.11f * scale);
                Vector3 lensScale = Vector3.one * (0.04f * scale);
                if (_cameraLensObject.transform.localPosition != lensPosition)
                    _cameraLensObject.transform.localPosition = lensPosition;
                if (_cameraLensObject.transform.localScale != lensScale)
                    _cameraLensObject.transform.localScale = lensScale;
            }
        }

        private RenderTexture CreateRenderTexture()
        {
            int width = Mathf.Clamp(_settings.RenderWidth, 64, 4096);
            int height = Mathf.Clamp(_settings.RenderHeight, 64, 4096);
            _settings.RenderWidth = width;
            _settings.RenderHeight = height;

            RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.name = "SubCameraDisplayProbeRT";
            renderTexture.useMipMap = false;
            renderTexture.autoGenerateMips = false;
            renderTexture.antiAliasing = 1;
            renderTexture.filterMode = ParseFilterMode(_settings.RenderFilterMode);
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            renderTexture.Create();
            return renderTexture;
        }

        private void RecreateRenderTexture(string reason)
        {
            if (_subCamera == null)
                return;

            StopVideoRecording("render-texture-recreate");
            RenderTexture old = _renderTexture;
            _subCamera.targetTexture = null;
            _renderTexture = CreateRenderTexture();
            _subCamera.targetTexture = _renderTexture;

            if (_displayMaterial != null)
                _displayMaterial.mainTexture = _renderTexture;
            if (_handPreviewMaterial != null)
                _handPreviewMaterial.mainTexture = _renderTexture;

            if (old != null)
            {
                old.Release();
                Destroy(old);
            }

            ApplyDisplaySettings();
            LogInfo("render texture recreated reason=" + reason + " size=" + _settings.RenderWidth + "x" + _settings.RenderHeight);
        }

        private void AttachCameraGizmo()
        {
            if (_cameraGizmo != null || _cameraAnchorObject == null)
                return;

            _cameraGizmo = TransformGizmoApi.Attach(_cameraAnchorObject);
            if (_cameraGizmo != null)
            {
                TransformGizmoApi.EnableCameraInputCaptureOnDrag(_cameraGizmo);
                _cameraGizmo.SetVisible(_settings.CameraGizmoVisible);
                _cameraGizmo.SetSizeMultiplier(_settings.GizmoSizeMultiplier);
                _cameraGizmo.DragStateChanged += OnGizmoDragStateChanged;
            }
        }

        private void AttachDisplayGizmo()
        {
            if (_displayGizmo != null || _displayAnchorObject == null)
                return;

            _displayGizmo = TransformGizmoApi.Attach(_displayAnchorObject);
            if (_displayGizmo != null)
            {
                TransformGizmoApi.EnableCameraInputCaptureOnDrag(_displayGizmo);
                _displayGizmo.SetVisible(_settings.DisplayGizmoVisible);
                _displayGizmo.SetSizeMultiplier(_settings.GizmoSizeMultiplier);
                _displayGizmo.DragStateChanged += OnGizmoDragStateChanged;
            }
        }

        private bool _cameraGizmoWasDragging;
        private void OnGizmoDragStateChanged(bool dragging)
        {
            _gizmoDragging = dragging;
            if (dragging)
            {
                _cameraGizmoWasDragging = _cameraGizmo != null && _cameraGizmo.IsDragging;
                return;
            }

            if (_cameraGizmoWasDragging) SaveMovedCameraPreset();
            else PersistTransformsToSettings();
            _cameraGizmoWasDragging = false;
        }

        private void SyncGizmoState()
        {
            if (_cameraGizmo != null)
            {
                _cameraGizmo.SetVisible(_settings.CameraGizmoVisible);
                _cameraGizmo.SetSizeMultiplier(_settings.GizmoSizeMultiplier);
            }

            if (_displayGizmo != null)
            {
                _displayGizmo.SetVisible(_settings.DisplayGizmoVisible);
                _displayGizmo.SetSizeMultiplier(_settings.GizmoSizeMultiplier);
            }

            if ((_cameraGizmo != null && _cameraGizmo.IsDragging) || (_displayGizmo != null && _displayGizmo.IsDragging))
            {
                _gizmoDragging = true;
            }
            else if (_gizmoDragging)
            {
                _gizmoDragging = false;
                if (_cameraGizmoWasDragging) SaveMovedCameraPreset();
                else PersistTransformsToSettings();
                _cameraGizmoWasDragging = false;
            }
        }

        private void ApplyCameraSettings()
        {
            if (_actorLease != 0) return;
            if (_subCamera == null || _cameraAnchorObject == null)
                return;

            if (_subCamera.fieldOfView != _settings.CameraFieldOfView)
                _subCamera.fieldOfView = _settings.CameraFieldOfView;
            if (_subCamera.nearClipPlane != _settings.CameraNearClip)
                _subCamera.nearClipPlane = _settings.CameraNearClip;
            if (_subCamera.farClipPlane != _settings.CameraFarClip)
                _subCamera.farClipPlane = _settings.CameraFarClip;
            if (_renderTexture != null)
            {
                FilterMode filter = ParseFilterMode(_settings.RenderFilterMode);
                if (_renderTexture.filterMode != filter)
                    _renderTexture.filterMode = filter;
            }
            ApplyCameraVisualScale();
            if (_boneFollowActive)
                return;

            Vector3 cameraPosition = new Vector3(_settings.CameraPosX, _settings.CameraPosY, _settings.CameraPosZ);
            Quaternion cameraRotation = Quaternion.Euler(_settings.CameraRotX, _settings.CameraRotY, _settings.CameraRotZ);
            if (_cameraAnchorObject.transform.localPosition != cameraPosition)
                _cameraAnchorObject.transform.localPosition = cameraPosition;
            if (_cameraAnchorObject.transform.localRotation != cameraRotation)
                _cameraAnchorObject.transform.localRotation = cameraRotation;
        }

        private void SyncCameraControlSliders(bool positionChanged, bool rotationChanged)
        {
            if (_actorLease != 0) { SyncExternalCameraSliders(positionChanged, rotationChanged); return; }
            if (_cameraAnchorObject == null)
                return;

            if (!_boneFollowActive)
            {
                ApplyCameraSettings();
                return;
            }

            if (positionChanged)
            {
                Transform bone = ResolveActiveBone();
                if (bone != null && _rootObject != null)
                {
                    _activeSaveCameraPosition = true;
                    Vector3 localPosition = new Vector3(_settings.CameraPosX, _settings.CameraPosY, _settings.CameraPosZ);
                    Vector3 desiredWorldPosition = _rootObject.transform.TransformPoint(localPosition);
                    _activeCameraOffsetLocal = desiredWorldPosition - bone.position;
                    UpdateBoneFollow();
                    PersistTransformsToSettings();
                    SaveActiveBonePresetOffsets();
                }
            }

            if (rotationChanged)
                SetStatus("ボーン追従中の回転は注視点で決定");
        }

        private void CopyCurrentCameraToSubCamera()
        {
            if (_actorLease != 0) return;
            EnsureCameraProbe();
            if (_cameraAnchorObject == null || _subCamera == null)
            {
                SetStatus("サブカメラ未生成");
                return;
            }

            Camera source = ResolveCurrentCameraCopySource();
            if (source == null)
            {
                SetStatus("コピー元カメラなし");
                return;
            }

            ClearBoneFollow();
            _cameraAnchorObject.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            _settings.CameraFieldOfView = Round2(Mathf.Clamp(source.fieldOfView, 10f, 170f));
            _settings.CameraNearClip = Round2(Mathf.Max(0.01f, source.nearClipPlane));
            _settings.CameraFarClip = Round2(Mathf.Max(_settings.CameraNearClip + 1f, source.farClipPlane));
            _subCamera.fieldOfView = _settings.CameraFieldOfView;
            _subCamera.nearClipPlane = _settings.CameraNearClip;
            _subCamera.farClipPlane = _settings.CameraFarClip;
            PersistTransformsToSettings();
            SetStatus("現在カメラをコピー: " + source.name);
        }

        private Camera ResolveCurrentCameraCopySource()
        {
            Camera main = Camera.main;
            if (IsCurrentCameraCopySource(main))
                return main;

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (IsCurrentCameraCopySource(camera))
                    return camera;
            }

            return null;
        }

        private bool IsCurrentCameraCopySource(Camera camera)
        {
            return camera != null
                && camera.isActiveAndEnabled
                && camera.targetTexture == null
                && camera != _subCamera
                && camera != _displayOverlayCamera;
        }

        private void ApplyDisplaySettings()
        {
            ApplyDisplayBrightness();
            if (_displayAnchorObject == null || _displayObject == null)
                return;

            Vector3 displayPosition = new Vector3(_settings.DisplayPosX, _settings.DisplayPosY, _settings.DisplayPosZ);
            Quaternion displayRotation = Quaternion.Euler(_settings.DisplayRotX, _settings.DisplayRotY, _settings.DisplayRotZ);
            if (_displayAnchorObject.transform.localPosition != displayPosition)
                _displayAnchorObject.transform.localPosition = displayPosition;
            if (_displayAnchorObject.transform.localRotation != displayRotation)
                _displayAnchorObject.transform.localRotation = displayRotation;
            _settings.DisplayHeight = Mathf.Max(0.1f, Round2(_settings.DisplayHeight));
            _settings.DisplayWidth = SettingsStore.CalculateDisplayWidth(_settings);
            Vector3 displayScale = new Vector3(_settings.DisplayWidth, _settings.DisplayHeight, 1f);
            if (_displayObject.transform.localScale != displayScale)
                _displayObject.transform.localScale = displayScale;

            ApplyDisplayLayer();
        }

        private void CaptureTransformsToSettings()
        {
            if (_actorLease == 0 && _cameraAnchorObject != null)
            {
                Vector3 pos = _cameraAnchorObject.transform.localPosition;
                Vector3 rot = _cameraAnchorObject.transform.localRotation.eulerAngles;
                _settings.CameraPosX = Round2(pos.x);
                _settings.CameraPosY = Round2(pos.y);
                _settings.CameraPosZ = Round2(pos.z);
                _settings.CameraRotX = Round2(rot.x);
                _settings.CameraRotY = Round2(rot.y);
                _settings.CameraRotZ = Round2(rot.z);
            }

            if (_displayAnchorObject != null)
            {
                Vector3 pos = _displayAnchorObject.transform.localPosition;
                Vector3 rot = _displayAnchorObject.transform.localRotation.eulerAngles;
                _settings.DisplayPosX = Round2(pos.x);
                _settings.DisplayPosY = Round2(pos.y);
                _settings.DisplayPosZ = Round2(pos.z);
                _settings.DisplayRotX = Round2(rot.x);
                _settings.DisplayRotY = Round2(rot.y);
                _settings.DisplayRotZ = Round2(rot.z);
            }
        }

        private void PersistTransformsToSettings()
        {
            CaptureTransformsToSettings();
            SaveSettings();
        }

        private void DestroyCameraOnly()
        {
            ReleaseActor();
            StopVideoRecording("camera-only-destroy");
            StopGrip();

            if (_cameraGizmo != null)
            {
                _cameraGizmo.DragStateChanged -= OnGizmoDragStateChanged;
                TransformGizmoApi.DisableCameraInputCaptureOnDrag(_cameraGizmo);
                _cameraGizmo = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_handPreviewObject != null)
            {
                Destroy(_handPreviewObject);
                _handPreviewObject = null;
            }
            if (_handPreviewMaterial != null)
            {
                Destroy(_handPreviewMaterial);
                _handPreviewMaterial = null;
            }

            if (_cameraAnchorObject != null)
            {
                Destroy(_cameraAnchorObject);
                _cameraAnchorObject = null;
            }

            _subCamera = null;
            _cameraVisualObject = null;
            _cameraLensObject = null;
            ClearBoneFollow();
            CancelTransition("camera-only-destroy");
            LogInfo("camera destroyed");
        }

        private void DestroyDisplayOnly()
        {
            if (_displayGizmo != null)
            {
                _displayGizmo.DragStateChanged -= OnGizmoDragStateChanged;
                TransformGizmoApi.DisableCameraInputCaptureOnDrag(_displayGizmo);
                _displayGizmo = null;
            }

            if (_displayMaterial != null)
            {
                Destroy(_displayMaterial);
                _displayMaterial = null;
            }

            if (_displayOverlayCameraObject != null)
            {
                Destroy(_displayOverlayCameraObject);
                _displayOverlayCameraObject = null;
                _displayOverlayCamera = null;
            }

            if (_displayAnchorObject != null)
            {
                Destroy(_displayAnchorObject);
                _displayAnchorObject = null;
            }

            _displayObject = null;
            LogInfo("display destroyed");
        }

        private void DestroyProbe()
        {
            ReleaseActor();
            StopVideoRecording("probe-destroy");
            StopGrip();

            if (_cameraGizmo != null)
                _cameraGizmo.DragStateChanged -= OnGizmoDragStateChanged;
            if (_displayGizmo != null)
                _displayGizmo.DragStateChanged -= OnGizmoDragStateChanged;
            if (_cameraGizmo != null)
                TransformGizmoApi.DisableCameraInputCaptureOnDrag(_cameraGizmo);
            if (_displayGizmo != null)
                TransformGizmoApi.DisableCameraInputCaptureOnDrag(_displayGizmo);

            _cameraGizmo = null;
            _displayGizmo = null;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_displayMaterial != null)
            {
                Destroy(_displayMaterial);
                _displayMaterial = null;
            }

            if (_handPreviewMaterial != null)
            {
                Destroy(_handPreviewMaterial);
                _handPreviewMaterial = null;
            }

            if (_displayOverlayCameraObject != null)
            {
                Destroy(_displayOverlayCameraObject);
                _displayOverlayCameraObject = null;
                _displayOverlayCamera = null;
            }

            if (_rootObject != null)
                Destroy(_rootObject);

            _rootObject = null;
            _cameraAnchorObject = null;
            _displayAnchorObject = null;
            _cameraVisualObject = null;
            _cameraLensObject = null;
            _displayObject = null;
            _handPreviewObject = null;
            _subCamera = null;
            ClearBoneFollow();
            LogInfo("probe destroyed");
        }

        private void SuspendForAstralModeInternal()
        {
            if (_suspendedForAstralMode)
                return;

            RestoreExternalRenderScope();
            _externalAstralStarted = Time.unscaledTime;
            _suspendedForAstralMode = true;
            StopVideoRecording("astral-suspend");
            StopGrip();
            DisableCharactersOnlyHook();
            ResetInputCaptureState();
            ReleaseInputCapture();

            if (_subCamera != null)
            {
                _subCamera.enabled = false;
                _subCamera.targetTexture = null;
            }
            if (_displayOverlayCamera != null)
                _displayOverlayCamera.enabled = false;
            if (_rootObject != null)
                _rootObject.SetActive(false);

            LogInfo("suspended for AstralMode");
        }

        private void ResumeFromAstralModeInternal()
        {
            if (!_suspendedForAstralMode)
                return;

            float pausedFor=Time.unscaledTime-_externalAstralStarted;
            _externalAcquiredAt+=pausedFor; _externalPresetStarted+=pausedFor;
            _transitionStartTime+=pausedFor; _autoCycleNextAt+=pausedFor; _autoCycleAwayUntil+=pausedFor;
            _suspendedForAstralMode = false;
            if (_rootObject != null)
                _rootObject.SetActive(true);
            if (_subCamera != null)
            {
                _subCamera.targetTexture = _renderTexture;
                _subCamera.enabled = true;
                if (_actorLease == 0) ApplyCameraSettings();
                if (_renderTexture != null)
                    _subCamera.Render();
            }
            if (_displayObject != null)
                ApplyDisplaySettings();
            SyncCharactersOnlyHook();

            LogInfo("resumed from AstralMode");
        }

        private static Camera ResolveWorldCamera()
        {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled)
                return main;

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
                    continue;
                return camera;
            }

            return null;
        }

        private void MoveProbeInFrontOfPlayer(bool initialCreate)
        {
            if (_rootObject == null)
                return;

            bool rootAtView = !initialCreate;
            if (TryResolveFrontRootPose(_settings.SpawnDistance, rootAtView, out Vector3 targetPos, out Quaternion targetRot, out string source))
            {
                _rootObject.transform.SetPositionAndRotation(targetPos, targetRot);
                if (!initialCreate)
                    ResetLocalLayoutForFront();
                LogInfo((initialCreate ? "probe spawn at front" : "probe moved to front")
                    + " source=" + source
                    + " root=" + targetPos.ToString("F3"));
                return;
            }

            LogWarn(initialCreate ? "probe spawn skipped: front anchor not found" : "probe move skipped: front anchor not found");
        }

        private void MoveCameraInFrontOfPlayer()
        {
            EnsureCamera();
            if (_cameraAnchorObject == null)
            {
                SetStatus("サブカメラ未生成");
                return;
            }

            if (!TryResolveFrontPlacement(_settings.SpawnDistance, Vector3.zero, out Vector3 targetPos, out _, out Quaternion forwardRot, out string source))
            {
                SetStatus("視点基準なし");
                return;
            }

            ClearBoneFollow();
            _cameraAnchorObject.transform.SetPositionAndRotation(targetPos, forwardRot);
            PersistTransformsToSettings();
            ApplyCameraSettings();
            LogInfo("camera moved to front source=" + source + " pos=" + targetPos.ToString("F3"));
            SetStatus("カメラを眼の前へ移動");
        }

        private void MoveDisplayInFrontOfPlayer()
        {
            EnsureDisplay();
            if (_displayAnchorObject == null)
            {
                SetStatus("ディスプレイ未生成");
                return;
            }

            if (!TryResolveFrontPlacement(_settings.SpawnDistance, Vector3.zero, out Vector3 targetPos, out _, out Quaternion displayRot, out string source))
            {
                SetStatus("視点基準なし");
                return;
            }

            _displayAnchorObject.transform.SetPositionAndRotation(targetPos, displayRot);
            PersistTransformsToSettings();
            ApplyDisplaySettings();
            LogInfo("display moved to front source=" + source + " pos=" + targetPos.ToString("F3"));
            SetStatus("ディスプレイを眼の前へ移動");
        }

        private static bool TryResolveFrontRootPose(
            float distance,
            bool rootAtView,
            out Vector3 position,
            out Quaternion rotation,
            out string source)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            source = "none";

            if (!TryResolveFrontReference(out Vector3 referencePosition, out Quaternion referenceRotation, out source))
                return false;

            float safeDistance = Mathf.Max(0.1f, distance);
            position = rootAtView
                ? referencePosition
                : referencePosition + referenceRotation * (Vector3.forward * safeDistance);
            rotation = referenceRotation;
            return true;
        }

        private static bool TryResolveFrontPlacement(
            float distance,
            Vector3 localOffset,
            out Vector3 position,
            out Quaternion facingPlayerRotation,
            out Quaternion displayRotation,
            out string source)
        {
            position = Vector3.zero;
            facingPlayerRotation = Quaternion.identity;
            displayRotation = Quaternion.identity;
            source = "none";

            if (!TryResolveFrontReference(out Vector3 referencePosition, out Quaternion referenceRotation, out source))
                return false;

            float safeDistance = Mathf.Max(0.1f, distance);
            Vector3 offset = localOffset + Vector3.forward * safeDistance;
            position = referencePosition + referenceRotation * offset;
            facingPlayerRotation = referenceRotation * Quaternion.Euler(0f, 180f, 0f);
            displayRotation = referenceRotation;
            return true;
        }

        private void ResetLocalLayoutForFront()
        {
            ClearBoneFollow();

            float distance = Mathf.Max(0.1f, _settings.SpawnDistance);

            _settings.CameraPosX = 0.45f;
            _settings.CameraPosY = -0.12f;
            _settings.CameraPosZ = distance;
            _settings.CameraRotX = 0f;
            _settings.CameraRotY = 0f;
            _settings.CameraRotZ = 0f;

            _settings.DisplayPosX = -0.45f;
            _settings.DisplayPosY = -0.12f;
            _settings.DisplayPosZ = distance;
            _settings.DisplayRotX = 0f;
            _settings.DisplayRotY = 0f;
            _settings.DisplayRotZ = 0f;

            ApplyCameraSettings();
            ApplyDisplaySettings();
            SaveSettings();
        }

        private static bool TryResolveFrontReference(out Vector3 position, out Quaternion rotation, out string source)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            source = "none";

            try
            {
                if (VR.Active)
                {
                    Transform head = ResolveVrHead();
                    if (head != null)
                    {
                        position = head.position;
                        rotation = head.rotation;
                        source = "VR.Head:" + head.name;
                        return true;
                    }

                    Camera vrMain = Camera.main;
                    if (vrMain != null && vrMain.isActiveAndEnabled && vrMain.GetComponent<SteamVR_Camera>() != null)
                    {
                        position = vrMain.transform.position;
                        rotation = vrMain.transform.rotation;
                        source = "VR.Camera.main:" + vrMain.name;
                        return true;
                    }

                    source = "VR.HeadMissing";
                    return false;
                }
            }
            catch
            {
                source = "VR.ResolveException";
                return false;
            }

            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled)
            {
                position = main.transform.position;
                rotation = ResolveDesktopFrontRotation(main.transform);
                source = "Camera.main:" + main.name;
                return true;
            }

            Transform fallback = ResolveSpawnAnchor();
            if (fallback == null)
                return false;

            position = fallback.position;
            rotation = ResolveDesktopFrontRotation(fallback);
            source = "Camera.fallback:" + fallback.name;
            return true;
        }

        private static Transform ResolveVrHead()
        {
            try
            {
                VRCamera camera = VR.Camera;
                if (camera == null || camera.SteamCam == null || camera.Head == null)
                    return null;
                return camera.Head;
            }
            catch
            {
                return null;
            }
        }

        private static Quaternion ResolveDesktopFrontRotation(Transform anchor)
        {
            Vector3 forward = anchor != null ? Vector3.ProjectOnPlane(anchor.forward, Vector3.up) : Vector3.forward;
            if (forward.sqrMagnitude < 1e-4f && anchor != null)
                forward = anchor.forward;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static Transform ResolveSpawnAnchor()
        {
            Camera main = Camera.main;
            return main != null && main.isActiveAndEnabled ? main.transform : null;
        }

        private void EnsureDisplayOverlayCamera()
        {
            if (!_settings.UseDisplayOverlayCamera)
                return;

            if (_displayOverlayCamera != null)
                return;

            _displayOverlayCameraObject = new GameObject("SubCameraDisplayOverlayCamera");
            DontDestroyOnLoad(_displayOverlayCameraObject);
            _displayOverlayCamera = _displayOverlayCameraObject.AddComponent<Camera>();
            _displayOverlayCamera.clearFlags = CameraClearFlags.Nothing;
            _displayOverlayCamera.cullingMask = 1 << Mathf.Clamp(_settings.DisplayLayer, 0, 30);
            _displayOverlayCamera.nearClipPlane = 0.01f;
            _displayOverlayCamera.farClipPlane = 1000f;
            _displayOverlayCamera.enabled = true;
            SyncDisplayOverlayCamera();
        }

        private Camera[] _displaySyncCameras;
        private int _displayMaskSyncedCameraCount = -1;
        private int _displayMaskSyncedMask;
        private bool _displayMaskSyncedOverlay;
        private Camera _displayMaskSyncedMain;
        private float _nextDisplayMaskSyncTime;

        private void SyncDisplayOverlayCamera()
        {
            int displayMask = 1 << Mathf.Clamp(_settings.DisplayLayer, 0, 30);
            bool useOverlay = _settings.UseDisplayOverlayCamera;
            int cameraCount = Camera.allCamerasCount;
            float now = Time.unscaledTime;
            if (!useOverlay && _displayOverlayCamera == null
                && _displayMaskSyncedCameraCount == cameraCount
                && _displayMaskSyncedMask == displayMask
                && !_displayMaskSyncedOverlay
                && now < _nextDisplayMaskSyncTime)
                return;

            Camera main = ResolveWorldCamera();
            if (_displayOverlayCamera == null)
            {
                if (useOverlay)
                    EnsureDisplayOverlayCamera();
            }

            if (_displayOverlayCamera != null && !useOverlay)
                _displayOverlayCamera.enabled = false;

            if (_displayOverlayCamera != null && useOverlay && main != null)
            {
                _displayOverlayCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                _displayOverlayCamera.fieldOfView = main.fieldOfView;
                _displayOverlayCamera.orthographic = main.orthographic;
                _displayOverlayCamera.orthographicSize = main.orthographicSize;
                _displayOverlayCamera.nearClipPlane = main.nearClipPlane;
                _displayOverlayCamera.farClipPlane = main.farClipPlane;
                _displayOverlayCamera.depth = main.depth + 100f;
                _displayOverlayCamera.cullingMask = displayMask;
                _displayOverlayCamera.clearFlags = CameraClearFlags.Nothing;
                _displayOverlayCamera.allowHDR = false;
                _displayOverlayCamera.allowMSAA = false;
                _displayOverlayCamera.enabled = true;
            }

            cameraCount = Camera.allCamerasCount;
            if (_displayMaskSyncedCameraCount == cameraCount
                && _displayMaskSyncedMask == displayMask
                && _displayMaskSyncedOverlay == useOverlay
                && _displayMaskSyncedMain == main
                && now < _nextDisplayMaskSyncTime)
                return;

            if (_displaySyncCameras == null || _displaySyncCameras.Length != cameraCount)
                _displaySyncCameras = new Camera[cameraCount];
            cameraCount = Camera.GetAllCameras(_displaySyncCameras);
            for (int i = 0; i < cameraCount; i++)
            {
                Camera camera = _displaySyncCameras[i];
                if (camera == null || camera == _displayOverlayCamera)
                    continue;
                if (camera == _subCamera)
                {
                    int currentMask = camera.cullingMask;
                    int mask = currentMask & ~displayMask;
                    if (currentMask != mask)
                        camera.cullingMask = mask;
                    continue;
                }

                if (camera.targetTexture == null)
                {
                    int currentMask = camera.cullingMask;
                    int mask = currentMask;
                    if (useOverlay)
                        mask &= ~displayMask;
                    else
                    {
                        if (camera == main)
                            mask |= displayMask;
                        else
                            mask &= ~displayMask;
                    }
                    if (currentMask != mask)
                        camera.cullingMask = mask;
                }
            }
            _displayMaskSyncedCameraCount = cameraCount;
            _displayMaskSyncedMask = displayMask;
            _displayMaskSyncedOverlay = useOverlay;
            _displayMaskSyncedMain = main;
            _nextDisplayMaskSyncTime = now + 1f;
        }

        private void ApplyDisplayLayer()
        {
            int layer = Mathf.Clamp(_settings.DisplayLayer, 0, 30);
            int displayChildren = _displayObject != null ? _displayObject.transform.childCount : 0;
            int previewChildren = _handPreviewObject != null ? _handPreviewObject.transform.childCount : 0;
            if (_layerSyncedValue != layer ||
                _layerSyncedDisplayObject != _displayObject ||
                _layerSyncedPreviewObject != _handPreviewObject ||
                _layerSyncedDisplayChildren != displayChildren ||
                _layerSyncedPreviewChildren != previewChildren ||
                (_displayObject != null && _displayObject.layer != layer) ||
                (_handPreviewObject != null && _handPreviewObject.layer != layer) ||
                Time.unscaledTime >= _nextDisplayLayerRefreshTime)
            {
                SetLayerRecursive(_displayObject, layer);
                SetLayerRecursive(_handPreviewObject, layer);
                _layerSyncedValue = layer;
                _layerSyncedDisplayObject = _displayObject;
                _layerSyncedPreviewObject = _handPreviewObject;
                _layerSyncedDisplayChildren = displayChildren;
                _layerSyncedPreviewChildren = previewChildren;
                _nextDisplayLayerRefreshTime = Time.unscaledTime + 1f;
            }
            SyncDisplayOverlayCamera();
        }

        private static void SetLayerRecursive(GameObject target, int layer)
        {
            if (target == null)
                return;

            if (target.layer != layer)
                target.layer = layer;
            Transform t = target.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        private static FilterMode ParseFilterMode(string raw)
        {
            string value = raw == null ? string.Empty : raw.Trim();
            if (string.Equals(value, "bilinear", System.StringComparison.OrdinalIgnoreCase))
                return FilterMode.Bilinear;
            if (string.Equals(value, "trilinear", System.StringComparison.OrdinalIgnoreCase))
                return FilterMode.Trilinear;
            return FilterMode.Point;
        }

        private void ApplyDisplayBrightness()
        {
            if (_displayMaterial == null || !_displayMaterial.HasProperty("_Color")) return;
            float brightness = _settings.DisplayBrightness;
            if (float.IsNaN(brightness) || float.IsInfinity(brightness)) brightness = 1f;
            brightness = Mathf.Clamp(brightness, 0f, 2f);
            var tint = new Color(brightness, brightness, brightness, 1f);
            if (_displayMaterial.color != tint) _displayMaterial.color = tint;
        }

        private static Material CreateDisplayMaterial(bool backFace)
        {
            Shader shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = backFace ? "SubCameraDisplayBackMaterial" : "SubCameraDisplayMaterial";
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetOverrideTag("Queue", "Transparent");
            if (material.HasProperty("_Cull"))
                material.SetInt("_Cull", 0);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_ZTest"))
                material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)(shader.name == "Sprites/Default" ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.SrcAlpha));
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return material;
        }
    }
}
