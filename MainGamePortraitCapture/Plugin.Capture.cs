using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace MainGamePortraitCapture
{
    public sealed partial class Plugin
    {
        private IEnumerator CaptureManySequence(int count)
        {
            _diagnosticBatchActive = true;
            _diagnosticBatchCount = count;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    _diagnosticShotIndex = i + 1;
                    LogInfo("batch shot begin shot=" + _diagnosticShotIndex + "/" + count
                        + " frame=" + Time.frameCount
                        + " realtime=" + Time.realtimeSinceStartup.ToString("0.000"));
                    TraceBoneDiagnostic("batch-before-sequence", true);
                    _captureBusy = true;
                    yield return CaptureSequence();
                    TraceBoneDiagnostic("batch-after-sequence", true);
                    LogInfo("batch shot end shot=" + _diagnosticShotIndex + "/" + count
                        + " frame=" + Time.frameCount
                        + " realtime=" + Time.realtimeSinceStartup.ToString("0.000"));
                }
            }
            finally
            {
                EndBoneDiagnostic();
                _diagnosticBatchActive = false;
                _diagnosticBatchCount = 0;
                _diagnosticShotIndex = 0;
            }
        }

        private sealed class CameraSnapshot
        {
            internal CameraControl_Ver2 Controller;
            internal BaseCameraControl_Ver2.CameraData Data;
            internal bool HasControllerData;
            internal Camera Camera;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal float Fov;
            internal float Aspect;
            internal float ControllerFov;
            internal float ControllerInitFov;
        }

        private sealed class ShotPose
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal float Fov;
            internal float Roll;
            internal float Pitch;
            internal float HeadPriority;
            internal float HipPriority;
            internal int Attempt;
            internal bool Recovery;
            internal float Distance;
        }

        private sealed class EyeLookSnapshot
        {
            internal ChaControl Female;
            internal EyeLookController Controller;
            internal Transform Target;
            internal bool Enabled;
            internal int Pattern;
        }

        private sealed class NeckLookSnapshot
        {
            internal ChaControl Female;
            internal NeckLookControllerVer2 Controller;
            internal Transform Target;
            internal bool Enabled;
            internal int Pattern;
        }

        private IEnumerator CaptureSequence()
        {
            _captureBusy = true;
            CameraSnapshot snapshot = null;
            EyeLookSnapshot eyeLookSnapshot = null;
            NeckLookSnapshot neckLookSnapshot = null;
            GameObject lookTargetObject = null;
            GameObject neckLookTargetObject = null;
            // 撮影が最後まで通ったか。異常終了時は設定に関わらずカメラを必ず戻す。
            bool captureCompleted = false;
            try
            {
                // Hシーンの最終姿勢はLateUpdateで確定する。
                // 連写2枚目以降も1枚目と同じ確定後の骨座標から移動先を計算する。
                yield return new WaitForEndOfFrame();

                SubjectPoints subject;
                string reason;
                if (!TryResolveSubject(out subject, out reason)) { LogError("capture rejected: " + reason); yield break; }
                BeginBoneDiagnostic(subject);
                CameraControl_Ver2 controller = ResolveCameraController();
                Camera camera = controller != null ? controller.thisCmaera : Camera.main;
                if (camera == null) { LogError("capture rejected: camera not found"); yield break; }
                snapshot = CaptureSnapshot(controller, camera);
                float captureAspect = Mathf.Max(1, _cfgCaptureWidth.Value) / (float)Mathf.Max(1, _cfgCaptureHeight.Value);

                ShotPose shot;
                float screenAspect = camera.aspect;
                try
                {
                    camera.aspect = captureAspect;
                    if (!TrySolveShot(camera, subject, out shot)) { LogError("capture rejected: no valid framing"); yield break; }
                }
                finally
                {
                    camera.aspect = screenAspect;
                }
                LogInfo("shot selected recovery=" + shot.Recovery + " attempt=" + shot.Attempt + " distance=" + shot.Distance.ToString("0.00") + " fov=" + shot.Fov.ToString("0.0") + " aspect=" + captureAspect.ToString("0.000") + " pitch=" + shot.Pitch.ToString("0.0") + " roll=" + shot.Roll.ToString("0.0") + " headPriority=" + shot.HeadPriority.ToString("0.00") + " hipPriority=" + shot.HipPriority.ToString("0.00"));

                _overrideCamera = camera;
                _cameraOverrideActive = true;
                LogCaptureLookState("before", subject, camera);
                yield return TweenCamera(snapshot.Position, snapshot.Rotation, snapshot.Fov, shot.Position, shot.Rotation, shot.Fov, _cfgMoveDuration.Value);
                TraceBoneDiagnostic("after-camera-tween", true);
                lookTargetObject = new GameObject("MainGamePortraitCaptureLookTarget");
                lookTargetObject.hideFlags = HideFlags.HideAndDontSave;
                lookTargetObject.transform.position = shot.Position;
                _captureLookTarget = lookTargetObject.transform;
                if (_cfgLookAtCameraDuringCapture.Value)
                {
                    eyeLookSnapshot = ApplyCaptureEyeLook(subject.Female, _captureLookTarget);
                    _captureEyeLookController = subject.Female.eyeLookCtrl;
                }
                bool followHeadPitch = _cfgHeadLookFollowPitchEnabled.Value;
                bool followHeadYaw = _cfgHeadLookFollowYawEnabled.Value;
                bool followHeadRoll = _cfgHeadLookFollowRollEnabled.Value;
                if (_cfgTurnHeadToCameraDuringCapture.Value && (followHeadPitch || followHeadYaw || followHeadRoll))
                {
                    float headPitch = followHeadPitch
                        ? RandomRangeExcluding(_cfgHeadLookMinPitch.Value, _cfgHeadLookMaxPitch.Value,
                            _cfgHeadLookPitchDeadZoneEnabled.Value, _cfgHeadLookPitchDeadZoneMin.Value, _cfgHeadLookPitchDeadZoneMax.Value)
                        : 0f;
                    float headYaw = followHeadYaw
                        ? RandomRangeExcluding(_cfgHeadLookMinYaw.Value, _cfgHeadLookMaxYaw.Value,
                            _cfgHeadLookYawDeadZoneEnabled.Value, _cfgHeadLookYawDeadZoneMin.Value, _cfgHeadLookYawDeadZoneMax.Value)
                        : 0f;
                    float headRoll = followHeadRoll
                        ? RandomRangeExcluding(_cfgHeadLookMinRoll.Value, _cfgHeadLookMaxRoll.Value,
                            _cfgHeadLookRollDeadZoneEnabled.Value, _cfgHeadLookRollDeadZoneMin.Value, _cfgHeadLookRollDeadZoneMax.Value)
                        : 0f;
                    CaptureHeadRoll(subject, headRoll);
                    if (followHeadPitch || followHeadYaw)
                    {
                        neckLookTargetObject = new GameObject("MainGamePortraitCaptureNeckLookTarget");
                        neckLookTargetObject.hideFlags = HideFlags.HideAndDontSave;
                        neckLookTargetObject.transform.position = ResolveHeadLookTarget(subject, shot.Position, headPitch, headYaw,
                            followHeadPitch, followHeadYaw);
                        _captureNeckLookTarget = neckLookTargetObject.transform;
                        neckLookSnapshot = ApplyCaptureNeckLook(subject.Female, _captureNeckLookTarget);
                        _captureNeckLookController = subject.Female.neckLookCtrl;
                    }
                    LogInfo("head look random pitch=" + headPitch.ToString("0.0") + " yaw=" + headYaw.ToString("0.0") + " roll=" + headRoll.ToString("0.0")
                        + " followPitch=" + followHeadPitch + " followYaw=" + followHeadYaw + " followRoll=" + followHeadRoll);
                }
                MaintainCaptureLookOverride();
                LogCaptureLookState("applied", subject, camera);
                yield return HoldCaptureLookStable(_cfgHoldBeforeCapture.Value);
                TraceBoneDiagnostic("after-stability-hold", true);

                // 画面に表示された撮影構図を、そのまま同じカメラで保存する。
                TraceBoneDiagnostic("before-end-of-frame", true);
                yield return new WaitForEndOfFrame();
                TraceBoneDiagnostic("after-end-of-frame", true);
                LogInfo("capture uses the visible selected shot without a second solve");
                LogCaptureLookState("capture", subject, camera);
                TraceBoneDiagnostic("before-camera-render", true);
                string path = CapturePng(camera);
                TraceBoneDiagnostic("after-camera-render", true);
                LogInfo("capture saved: " + path);
                // 「撮影後に元のカメラ位置へ戻す」がoffなら、撮った構図のまま留まる。
                if (_cfgReturnCamera.Value)
                {
                    yield return TweenCamera(shot.Position, shot.Rotation, shot.Fov, snapshot.Position, snapshot.Rotation, snapshot.Fov, _cfgReturnDuration.Value);
                }
                else
                {
                    // 留まる場合、カメラの位置を書くだけでは足りない。
                    // CameraControl_Ver2 は毎フレーム自分の CameraData に従って
                    // カメラを動かすため、中身が古い構図のままだと次のフレームで
                    // 元へ引き戻される。撮影構図をコントローラ自身へ書き込む。
                    ApplyShotToController(controller, camera, shot, subject);
                    LogInfo("camera kept at capture pose (return disabled)");
                    // 撮影後に何が・いつ巻き戻るかを記録する。
                    // 「戻る」と言われても、位置なのか画角なのか、誰が戻したのかが
                    // ログに無いと特定できない。
                    StartCoroutine(HoldCaptureFov(controller, camera, shot));
                }
                ClearCaptureLookOverride();
                RestoreNeckLook(neckLookSnapshot);
                neckLookSnapshot = null;
                RestoreEyeLook(eyeLookSnapshot);
                eyeLookSnapshot = null;
                captureCompleted = true;
            }
            finally
            {
                TraceBoneDiagnostic("cleanup-enter", true);
                _cameraOverrideActive = false;
                ClearCaptureLookOverride();
                TraceBoneDiagnostic("cleanup-after-clear-look", true);
                RestoreNeckLook(neckLookSnapshot);
                TraceBoneDiagnostic("cleanup-after-restore-neck", true);
                RestoreEyeLook(eyeLookSnapshot);
                TraceBoneDiagnostic("cleanup-after-restore-eye", true);
                // 正常終了かつ「戻さない」設定の時だけ、復帰を省く。
                if (snapshot != null && (!captureCompleted || _cfgReturnCamera.Value))
                    RestoreSnapshot(snapshot);
                if (lookTargetObject != null) UnityEngine.Object.Destroy(lookTargetObject);
                if (neckLookTargetObject != null) UnityEngine.Object.Destroy(neckLookTargetObject);
                _overrideCamera = null;
                _captureBusy = false;
                TraceBoneDiagnostic("cleanup-exit", true);
                if (!_diagnosticBatchActive) EndBoneDiagnostic();
            }
        }

        private void MaintainCaptureLookOverride()
        {
            if (_captureLookTarget == null) return;
            if (_captureEyeLookController != null)
            {
                _captureEyeLookController.target = _captureLookTarget;
                _captureEyeLookController.enabled = true;
                _captureEyeLookController.ptnNo = _cfgEyeLookPatternDuringCapture.Value;
            }
            if (_captureNeckLookController != null)
            {
                _captureNeckLookController.target = _captureNeckLookTarget != null ? _captureNeckLookTarget : _captureLookTarget;
                _captureNeckLookController.enabled = true;
                _captureNeckLookController.ptnNo = _cfgNeckLookPatternDuringCapture.Value;
                _captureNeckLookController.rate = 1f;
            }
        }

        private void ApplyCaptureLookForSettlingFrame()
        {
            MaintainCaptureLookOverride();
            if (_captureNeckLookController != null)
                _captureNeckLookController.ForceLateUpdate();
            ApplyCaptureHeadTiltForRender();
            if (_captureEyeLookController != null)
                _captureEyeLookController.ForceLateUpdate();
        }

        private IEnumerator HoldCaptureLookStable(float duration)
        {
            float endTime = Time.realtimeSinceStartup + Mathf.Max(0f, duration);
            do
            {
                ApplyCaptureLookForSettlingFrame();
                yield return null;
            }
            while (Time.realtimeSinceStartup < endTime);
        }

        private void ApplyCaptureEyeLookForRender()
        {
            if (_captureLookTarget == null || _captureEyeLookController == null) return;
            _captureEyeLookController.target = _captureLookTarget;
            _captureEyeLookController.enabled = true;
            _captureEyeLookController.ptnNo = _cfgEyeLookPatternDuringCapture.Value;
            _captureEyeLookController.ForceLateUpdate();
        }

        private void CaptureHeadRoll(SubjectPoints subject, float randomRoll)
        {
            _captureTiltHead = null;
            _captureHeadRollOverrideActive = false;
            if (subject == null || subject.Head == null) return;

            Vector3 forward = subject.Head.forward.normalized;
            Vector3 referenceUp = Vector3.ProjectOnPlane(subject.BodyUp, forward);
            Vector3 headUp = Vector3.ProjectOnPlane(subject.Head.up, forward);
            if (referenceUp.sqrMagnitude < 0.0001f || headUp.sqrMagnitude < 0.0001f) return;

            _captureTiltHead = subject.Head;
            _captureTiltBodyUp = subject.BodyUp.normalized;
            float originalRoll = _cfgPreserveHeadTiltDuringCapture.Value
                ? Vector3.SignedAngle(referenceUp.normalized, headUp.normalized, forward)
                : 0f;
            _captureHeadTiltDegrees = originalRoll + randomRoll;
            _captureHeadRollOverrideActive = _cfgPreserveHeadTiltDuringCapture.Value || Mathf.Abs(randomRoll) > 0.01f;
            LogDebug("head roll original=" + originalRoll.ToString("0.0") + " random=" + randomRoll.ToString("0.0") + " final=" + _captureHeadTiltDegrees.ToString("0.0"));
        }

        private void ApplyCaptureHeadTiltForRender()
        {
            Transform head = _captureTiltHead;
            if (head == null || !_captureHeadRollOverrideActive) return;

            Vector3 forward = head.forward.normalized;
            Vector3 referenceUp = Vector3.ProjectOnPlane(_captureTiltBodyUp, forward);
            Vector3 currentUp = Vector3.ProjectOnPlane(head.up, forward);
            if (referenceUp.sqrMagnitude < 0.0001f || currentUp.sqrMagnitude < 0.0001f) return;

            Vector3 desiredUp = Quaternion.AngleAxis(_captureHeadTiltDegrees, forward) * referenceUp.normalized;
            float correction = Vector3.SignedAngle(currentUp.normalized, desiredUp.normalized, forward);
            head.rotation = Quaternion.AngleAxis(correction, forward) * head.rotation;
        }

        private void ClearCaptureLookOverride()
        {
            _captureEyeLookController = null;
            _captureNeckLookController = null;
            _captureLookTarget = null;
            _captureNeckLookTarget = null;
            _captureTiltHead = null;
            _captureTiltBodyUp = Vector3.zero;
            _captureHeadTiltDegrees = 0f;
            _captureHeadRollOverrideActive = false;
        }

        private Vector3 ResolveHeadLookTarget(SubjectPoints subject, Vector3 cameraPosition, float pitch, float yaw, bool followPitch, bool followYaw)
        {
            Vector3 origin = subject.Head.position;
            Vector3 cameraDirection = cameraPosition - origin;
            float distance = Mathf.Max(0.5f, cameraDirection.magnitude);
            cameraDirection.Normalize();

            Vector3 bodyUp = subject.BodyUp.sqrMagnitude > 0.0001f ? subject.BodyUp.normalized : Vector3.up;
            Vector3 originalDirection = subject.HeadForward.sqrMagnitude > 0.0001f ? subject.HeadForward.normalized : cameraDirection;
            Vector3 horizontal = Vector3.ProjectOnPlane(followYaw ? cameraDirection : originalDirection, bodyUp);
            if (horizontal.sqrMagnitude < 0.0001f) horizontal = subject.CameraOut;
            if (horizontal.sqrMagnitude < 0.0001f) horizontal = Vector3.forward;
            horizontal.Normalize();
            if (followYaw) horizontal = Quaternion.AngleAxis(yaw, bodyUp) * horizontal;

            Vector3 pitchSource = followPitch ? cameraDirection : originalDirection;
            float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(pitchSource, bodyUp), -1f, 1f)) * Mathf.Rad2Deg;
            if (followPitch) elevation += pitch;
            Vector3 direction = Quaternion.AngleAxis(-elevation, Vector3.Cross(bodyUp, horizontal).normalized) * horizontal;
            return origin + direction.normalized * distance;
        }

        private static float RandomRange(float a, float b)
        {
            return UnityEngine.Random.Range(Mathf.Min(a, b), Mathf.Max(a, b));
        }

        private static float RandomRangeExcluding(float a, float b, bool deadZoneEnabled, float deadZoneA, float deadZoneB)
        {
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);
            if (!deadZoneEnabled || max - min <= 0.0001f) return RandomRange(min, max);

            float deadMin = Mathf.Max(min, Mathf.Min(deadZoneA, deadZoneB));
            float deadMax = Mathf.Min(max, Mathf.Max(deadZoneA, deadZoneB));
            if (deadMax <= deadMin) return RandomRange(min, max);

            float leftLength = Mathf.Max(0f, deadMin - min);
            float rightLength = Mathf.Max(0f, max - deadMax);
            float usableLength = leftLength + rightLength;
            if (usableLength <= 0.0001f) return min;

            float pick = UnityEngine.Random.Range(0f, usableLength);
            return pick < leftLength
                ? UnityEngine.Random.Range(min, deadMin)
                : UnityEngine.Random.Range(deadMax, max);
        }

        private EyeLookSnapshot ApplyCaptureEyeLook(ChaControl female, Transform cameraTarget)
        {
            if (female == null || female.eyeLookCtrl == null || cameraTarget == null) return null;
            EyeLookSnapshot saved = new EyeLookSnapshot
            {
                Female = female,
                Controller = female.eyeLookCtrl,
                Target = female.eyeLookCtrl.target,
                Enabled = female.eyeLookCtrl.enabled,
                Pattern = female.GetLookEyesPtn()
            };
            female.eyeLookCtrl.target = cameraTarget;
            female.eyeLookCtrl.enabled = true;
            female.ChangeLookEyesPtn(_cfgEyeLookPatternDuringCapture.Value);
            LogDebug("eye look redirected to capture camera pattern=" + _cfgEyeLookPatternDuringCapture.Value);
            return saved;
        }

        private void RestoreEyeLook(EyeLookSnapshot saved)
        {
            if (saved == null || saved.Controller == null) return;
            saved.Controller.target = saved.Target;
            saved.Controller.enabled = saved.Enabled;
            if (saved.Female != null) saved.Female.ChangeLookEyesPtn(saved.Pattern);
            LogDebug("eye look restored");
        }

        private NeckLookSnapshot ApplyCaptureNeckLook(ChaControl female, Transform cameraTarget)
        {
            if (female == null || female.neckLookCtrl == null || cameraTarget == null) return null;
            NeckLookSnapshot saved = new NeckLookSnapshot
            {
                Female = female,
                Controller = female.neckLookCtrl,
                Target = female.neckLookCtrl.target,
                Enabled = female.neckLookCtrl.enabled,
                Pattern = female.GetLookNeckPtn()
            };
            female.neckLookCtrl.target = cameraTarget;
            female.neckLookCtrl.enabled = true;
            female.ChangeLookNeckPtn(_cfgNeckLookPatternDuringCapture.Value, 1f);
            LogDebug("neck look redirected to capture camera pattern=" + _cfgNeckLookPatternDuringCapture.Value);
            return saved;
        }

        private void RestoreNeckLook(NeckLookSnapshot saved)
        {
            if (saved == null || saved.Controller == null) return;
            saved.Controller.target = saved.Target;
            saved.Controller.enabled = saved.Enabled;
            if (saved.Female != null) saved.Female.ChangeLookNeckPtn(saved.Pattern, 1f);
            LogDebug("neck look restored");
        }

        private void LogCaptureLookState(string phase, SubjectPoints subject, Camera camera)
        {
            if (!_runtimeLoggingEnabled || _cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value || subject == null || subject.Female == null) return;
            Transform head = subject.Head;
            Transform eyeTarget = subject.Female.eyeLookCtrl != null ? subject.Female.eyeLookCtrl.target : null;
            Transform neckTarget = subject.Female.neckLookCtrl != null ? subject.Female.neckLookCtrl.target : null;
            Vector3 headPosition = head != null ? head.position : subject.Center;
            Vector3 cameraDirection = camera != null ? (camera.transform.position - headPosition).normalized : Vector3.zero;
            Vector3 headForward = head != null ? head.forward : Vector3.zero;
            Vector3 eyeForward = subject.Female.eyeLookCtrl != null ? subject.Female.eyeLookCtrl.transform.forward : Vector3.zero;
            Vector3 neckForward = subject.Female.neckLookCtrl != null ? subject.Female.neckLookCtrl.transform.forward : Vector3.zero;
            LogDebug("look phase=" + phase
                + " cameraPos=" + (camera != null ? camera.transform.position.ToString() : "null")
                + " headPos=" + headPosition
                + " cameraDir=" + cameraDirection
                + " headForward=" + headForward
                + " headAngle=" + Vector3.Angle(headForward, cameraDirection).ToString("0.0")
                + " eyeTarget=" + DescribeLookTarget(eyeTarget)
                + " eyeForward=" + eyeForward
                + " eyeAngle=" + Vector3.Angle(eyeForward, cameraDirection).ToString("0.0")
                + " eyePattern=" + subject.Female.GetLookEyesPtn()
                + " neckTarget=" + DescribeLookTarget(neckTarget)
                + " neckForward=" + neckForward
                + " neckAngle=" + Vector3.Angle(neckForward, cameraDirection).ToString("0.0")
                + " neckPattern=" + subject.Female.GetLookNeckPtn());
        }

        private static string DescribeLookTarget(Transform target)
        {
            return target != null ? target.name + "@" + target.position : "null";
        }

        private CameraControl_Ver2 ResolveCameraController()
        {
            HFlag flag = UnityEngine.Object.FindObjectOfType<HFlag>();
            if (flag != null && flag.ctrlCamera != null) return flag.ctrlCamera;
            return UnityEngine.Object.FindObjectOfType<CameraControl_Ver2>();
        }

        private static CameraSnapshot CaptureSnapshot(CameraControl_Ver2 controller, Camera camera)
        {
            return new CameraSnapshot
            {
                Controller = controller,
                Data = controller != null ? controller.GetCameraData() : default(BaseCameraControl_Ver2.CameraData),
                HasControllerData = controller != null,
                Camera = camera,
                Position = camera.transform.position,
                Rotation = camera.transform.rotation,
                Fov = camera.fieldOfView,
                Aspect = camera.aspect,
                ControllerFov = controller != null ? controller.CameraFov : camera.fieldOfView,
                ControllerInitFov = controller != null ? controller.CameraInitFov : camera.fieldOfView
            };
        }

        /// <summary>
        /// 撮影した構図を CameraControl_Ver2 の CameraData として書き込む。
        /// カメラの transform を直接書くだけでは、コントローラが次のフレームで
        /// 自分の値へ引き戻すため、その場に留まらない。
        /// 角度はワールドで組み立て、最後にコントローラのローカルへ畳む。
        /// </summary>
        /// <summary>
        /// 撮影後、撮った画角を握り続ける。
        ///
        /// MainGameCameraControl は毎フレーム自分の値でFOVを再適用し、
        /// 外部から渡す口(TryApplyExternalFov)も設計上1秒しか保持しない。
        /// 一度書いて終わりでは必ず解かれるので、こちらが主張し続ける。
        ///
        /// 手放す条件は「使用者がカメラを動かした時」。
        /// 位置か向きが動いたらズーム操作も含めて本人の意思とみなし、以後は触らない。
        /// </summary>
        private IEnumerator HoldCaptureFov(CameraControl_Ver2 controller, Camera camera, ShotPose shot)
        {
            if (camera == null) yield break;

            Vector3 heldPos = camera.transform.position;
            Quaternion heldRot = camera.transform.rotation;
            float wantFov = shot.Fov;
            float start = Time.unscaledTime;
            float nextNotify = 0f;
            float nextStolenLog = 0f;
            int reassert = 0;

            while (true)
            {
                yield return null;
                if (camera == null) yield break;

                // 次の撮影が始まったら譲る。
                if (_captureBusy) yield break;

                // 使用者がカメラを動かしたら手を離す。
                if (Vector3.Distance(camera.transform.position, heldPos) > 0.02f
                    || Quaternion.Angle(camera.transform.rotation, heldRot) > 0.5f)
                {
                    LogInfo("capture fov hold released (camera moved by user) after "
                        + (Time.unscaledTime - start).ToString("0.0") + "s reasserts=" + reassert);
                    yield break;
                }

                // 毎フレーム主張する。
                // 0.25秒ごとでは相手の書き込みに負け、大半の時間が相手の値になる。
                float stolen = camera.fieldOfView;
                bool wasStolen = Mathf.Abs(stolen - wantFov) > 0.01f;
                if (wasStolen) reassert++;

                // 強制維持は設定が有効な時だけ。
                // 相手が何のためにFOVを書いているか分からないうちは踏み潰さない。
                if (_cfgForceKeepFov != null && _cfgForceKeepFov.Value)
                {
                    if (controller != null)
                    {
                        try
                        {
                            BaseCameraControl_Ver2.CameraData d = controller.GetCameraData();
                            d.Fov = wantFov;
                            controller.SetCameraData(d);
                            controller.CameraFov = wantFov;
                            controller.CameraInitFov = wantFov;
                        }
                        catch { }
                    }
                    camera.fieldOfView = wantFov;

                    // 外部への宣言だけは頻度を落とす(相手側が1秒保持する作りのため)。
                    if (Time.unscaledTime >= nextNotify)
                    {
                        nextNotify = Time.unscaledTime + 0.25f;
                        NotifyExternalFov(wantFov);
                    }
                }

                // 奪われた時に「相手が入れた値」を記録する。
                // 値が分かれば誰の仕業か決まる(周期的に動くならビート連動のズーム等)。
                if (wasStolen && Time.unscaledTime >= nextStolenLog)
                {
                    nextStolenLog = Time.unscaledTime + 1f;
                    float dataFov = -1f;
                    float ctrlFov = -1f;
                    try
                    {
                        if (controller != null)
                        {
                            dataFov = controller.GetCameraData().Fov;
                            ctrlFov = controller.CameraFov;
                        }
                    }
                    catch { }
                    LogInfo("capture fov stolen count=" + reassert
                        + " want=" + wantFov.ToString("0.###")
                        + " theirValue=" + stolen.ToString("0.###")
                        + " diff=" + (stolen - wantFov).ToString("0.###")
                        + " controllerData.Fov=" + dataFov.ToString("0.###")
                        + " controller.CameraFov=" + ctrlFov.ToString("0.###")
                        + " externalFov=" + LastExternalFovResult);
                }

                // 位置を掴み直す(こちらの書き込みで微動した分を基準に含める)。
                heldPos = camera.transform.position;
                heldRot = camera.transform.rotation;
            }
        }

        private void ApplyShotToController(CameraControl_Ver2 controller, Camera camera, ShotPose shot, SubjectPoints subject)
        {
            if (camera != null)
            {
                camera.transform.position = shot.Position;
                camera.transform.rotation = shot.Rotation;
                camera.fieldOfView = shot.Fov;
            }

            if (controller == null) return;

            try
            {
                // 注視点はカメラ前方、被写体までの距離に置く。
                float distance = subject != null
                    ? Vector3.Distance(shot.Position, subject.Center)
                    : 1f;
                if (distance < 0.01f) distance = 1f;

                Vector3 lookAt = shot.Position + shot.Rotation * Vector3.forward * distance;
                Quaternion worldRot = shot.Rotation;

                BaseCameraControl_Ver2.CameraData data = controller.GetCameraData();
                if (controller.transBase != null)
                {
                    data.Pos = controller.transBase.InverseTransformPoint(lookAt);
                    data.Rot = NormalizeEuler((Quaternion.Inverse(controller.transBase.rotation) * worldRot).eulerAngles);
                }
                else
                {
                    data.Pos = lookAt;
                    data.Rot = NormalizeEuler(worldRot.eulerAngles);
                }

                data.Dir = Quaternion.Inverse(worldRot) * (shot.Position - lookAt);
                data.Fov = shot.Fov;

                controller.SetCameraData(data);
                controller.CameraFov = shot.Fov;
                controller.CameraInitFov = shot.Fov;

                // FOV はここに書くだけでは保たない。
                // MainGameCameraControl が毎フレーム自分の値で上書きし直すため、
                // 向こうの公開APIでも「この画角を使え」と宣言しておく。
                NotifyExternalFov(shot.Fov);

                LogInfo("capture framing written distance=" + distance.ToString("0.00")
                    + " fov=" + shot.Fov.ToString("0.##")
                    + " externalFov=" + LastExternalFovResult);
            }
            catch (Exception ex)
            {
                LogError("failed to write capture framing to controller: " + ex.Message);
            }
        }

        private static bool _externalFovResolved;
        private static System.Reflection.MethodInfo _externalFovMethod;

        /// <summary>
        /// MainGameCameraControl へ「この画角を使え」と伝える。
        /// 参照を増やしたくないので反射で呼ぶ。相手が居なければ黙って諦める。
        /// </summary>
        private static void NotifyExternalFov(float fov)
        {
            if (!_externalFovResolved)
            {
                _externalFovResolved = true;
                try
                {
                    System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length; i++)
                    {
                        System.Type type = null;
                        try { type = assemblies[i].GetType("MainGameCameraControl.Plugin", false); }
                        catch { continue; }
                        if (type == null) continue;

                        _externalFovMethod = type.GetMethod(
                            "TryApplyExternalFov",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                            null,
                            new[] { typeof(float), typeof(string) },
                            null);
                        if (_externalFovMethod != null) break;
                    }
                }
                catch { _externalFovMethod = null; }
            }

            if (_externalFovMethod == null)
            {
                LastExternalFovResult = "MainGameCameraControl.TryApplyExternalFov not found";
                return;
            }

            try
            {
                object r = _externalFovMethod.Invoke(null, new object[] { fov, "MainGamePortraitCapture" });
                LastExternalFovResult = "TryApplyExternalFov(" + fov.ToString("0.##") + ") -> " + r;
            }
            catch (Exception ex)
            {
                LastExternalFovResult = "TryApplyExternalFov threw: " + ex.Message;
            }
        }

        /// <summary>直近の外部FOV通知の結果。握り潰さずログに出すため。</summary>
        private static string LastExternalFovResult = "(not called)";

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            else if (angle < -180f) angle += 360f;
            return angle;
        }

        private void RestoreSnapshot(CameraSnapshot snapshot)
        {
            if (snapshot.Controller != null && snapshot.HasControllerData)
            {
                snapshot.Controller.SetCameraData(snapshot.Data);
                snapshot.Controller.CameraFov = snapshot.ControllerFov;
                snapshot.Controller.CameraInitFov = snapshot.ControllerInitFov;
            }
            if (snapshot.Camera != null)
            {
                snapshot.Camera.transform.position = snapshot.Position;
                snapshot.Camera.transform.rotation = snapshot.Rotation;
                snapshot.Camera.fieldOfView = snapshot.Fov;
                snapshot.Camera.aspect = snapshot.Aspect;
            }
            LogDebug("camera restored");
        }

        private bool TrySolveShot(Camera camera, SubjectPoints subject, out ShotPose result)
        {
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            result = null;
            int blockedCount = 0;
            int outsideCount = 0;
            int testedCount = 0;
            string firstBlocker = string.Empty;
            try
            {
                Vector3 bodyUp = subject.BodyUp;
                int attempts = Mathf.Max(1, _cfgRandomAttempts.Value);
                const int recoveryAttempts = 9;
                for (int attempt = 1; attempt <= attempts + recoveryAttempts; attempt++)
                {
                    bool recovery = attempt > attempts;
                    float actualHeadPriority;
                    float actualHipPriority;
                    Vector3 baseOut = ResolveBlendedCameraOut(subject, true, out actualHeadPriority, out actualHipPriority);
                    int recoveryIndex = recovery ? attempt - attempts - 1 : -1;
                    float yaw = UnityEngine.Random.Range(Mathf.Min(_cfgMinYaw.Value, _cfgMaxYaw.Value), Mathf.Max(_cfgMinYaw.Value, _cfgMaxYaw.Value));
                    float pitch = recovery ? 0f : UnityEngine.Random.Range(Mathf.Min(_cfgMinPitch.Value, _cfgMaxPitch.Value), Mathf.Max(_cfgMinPitch.Value, _cfgMaxPitch.Value));
                    float roll = UnityEngine.Random.Range(Mathf.Min(_cfgMinRoll.Value, _cfgMaxRoll.Value), Mathf.Max(_cfgMinRoll.Value, _cfgMaxRoll.Value));
                    float fov = UnityEngine.Random.Range(Mathf.Min(_cfgMinFov.Value, _cfgMaxFov.Value), Mathf.Max(_cfgMinFov.Value, _cfgMaxFov.Value));
                    Vector3 yawedOutward = Quaternion.AngleAxis(yaw, bodyUp) * baseOut;
                    Vector3 right = Vector3.Cross(bodyUp, yawedOutward).normalized;
                    Vector3 outward;
                    if (recovery)
                    {
                        // The configured range failed. Do not walk outward in fixed
                        // increments: construct a floor-safe world elevation directly.
                        Vector3 horizontal = Vector3.ProjectOnPlane(yawedOutward, Vector3.up);
                        if (horizontal.sqrMagnitude < 0.0001f)
                            horizontal = Vector3.ProjectOnPlane(subject.BodyRight, Vector3.up);
                        horizontal.Normalize();
                        float safeElevation = UnityEngine.Random.Range(10f, 50f);
                        float radians = safeElevation * Mathf.Deg2Rad;
                        outward = (horizontal * Mathf.Cos(radians) + Vector3.up * Mathf.Sin(radians)).normalized;
                        if (right.sqrMagnitude > 0.0001f)
                            pitch = -Vector3.SignedAngle(yawedOutward, outward, right);
                    }
                    else
                    {
                        outward = Quaternion.AngleAxis(-pitch, right) * yawedOutward;
                    }
                    for (float distance = Mathf.Max(0.2f, _cfgMinDistance.Value); distance <= Mathf.Max(_cfgMinDistance.Value, _cfgMaxDistance.Value) + 0.001f; distance += 0.05f)
                    {
                        testedCount++;
                        Vector3 position = subject.Center + outward * distance;
                        string blocker;
                        if (HasBlockingGeometry(subject, position, out blocker))
                        {
                            blockedCount++;
                            if (string.IsNullOrEmpty(firstBlocker)) firstBlocker = blocker;
                            continue;
                        }
                        Vector3 lookDirection = subject.Center - position;
                        Quaternion rotation = Quaternion.LookRotation(lookDirection, ResolveImageUp(lookDirection, subject));
                        rotation = Quaternion.AngleAxis(roll, lookDirection.normalized) * rotation;
                        camera.transform.position = position;
                        camera.transform.rotation = rotation;
                        camera.fieldOfView = Mathf.Clamp(fov, 5f, 120f);
                        if (!AllPointsInside(camera, subject.FitPoints, _cfgViewportMargin.Value))
                        {
                            outsideCount++;
                            continue;
                        }
                        result = new ShotPose { Position = position, Rotation = rotation, Fov = camera.fieldOfView, Roll = roll, Pitch = pitch, HeadPriority = actualHeadPriority, HipPriority = actualHipPriority, Attempt = recovery ? recoveryIndex + 1 : attempt, Recovery = recovery, Distance = distance };
                        return true;
                    }
                }
                LogDebug("framing failed tested=" + testedCount + " blocked=" + blockedCount + " outside=" + outsideCount + " firstBlocker=" + (string.IsNullOrEmpty(firstBlocker) ? "none" : firstBlocker) + " pitch=" + _cfgMinPitch.Value.ToString("0.0") + ".." + _cfgMaxPitch.Value.ToString("0.0") + " distance=" + _cfgMinDistance.Value.ToString("0.00") + ".." + _cfgMaxDistance.Value.ToString("0.00"));
                return false;
            }
            finally
            {
                camera.transform.position = savedPosition;
                camera.transform.rotation = savedRotation;
                camera.fieldOfView = savedFov;
            }
        }

        private Vector3 ResolveBlendedCameraOut(SubjectPoints subject, bool randomize, out float headPriority, out float hipPriority)
        {
            headPriority = Mathf.Max(0f, _cfgHeadDirectionPriority.Value);
            hipPriority = Mathf.Max(0f, _cfgHipDirectionPriority.Value);
            if (randomize)
            {
                float randomness = Mathf.Clamp01(_cfgDirectionPriorityRandomness.Value);
                headPriority *= UnityEngine.Random.Range(1f - randomness, 1f + randomness);
                hipPriority *= UnityEngine.Random.Range(1f - randomness, 1f + randomness);
            }

            Vector3 headOut = Vector3.ProjectOnPlane(subject.HeadForward, subject.BodyUp);
            Vector3 hipOut = Vector3.ProjectOnPlane(subject.HipForward, subject.BodyUp);
            if (headOut.sqrMagnitude < 0.0001f) headOut = subject.CameraOut;
            if (hipOut.sqrMagnitude < 0.0001f) hipOut = subject.CameraOut;
            headOut.Normalize();
            hipOut.Normalize();

            Vector3 blended = headOut * headPriority + hipOut * hipPriority;
            return blended.sqrMagnitude >= 0.0001f ? blended.normalized : subject.CameraOut;
        }

        private Vector3 ResolveImageUp(Vector3 lookDirection, SubjectPoints subject)
        {
            Vector3 viewForward = lookDirection.normalized;

            // For a lying pose, make the hips-to-head axis vertical in the photo.
            // Standing and inverted-standing poses retain the world horizon.
            float uprightness = Mathf.Abs(Vector3.Dot(subject.BodyUp, Vector3.up));
            bool alignToBody = _cfgAutoRotateLyingPose.Value && uprightness < 0.5f;
            Vector3 preferredUp = alignToBody ? subject.BodyUp : Vector3.up;
            Vector3 imageUp = preferredUp - viewForward * Vector3.Dot(preferredUp, viewForward);
            if (imageUp.sqrMagnitude < 0.01f)
            {
                Vector3 fallbackUp = alignToBody ? Vector3.up : subject.BodyUp;
                imageUp = fallbackUp - viewForward * Vector3.Dot(fallbackUp, viewForward);
            }
            if (imageUp.sqrMagnitude < 0.01f)
                imageUp = subject.BodyRight - viewForward * Vector3.Dot(subject.BodyRight, viewForward);
            return imageUp.sqrMagnitude >= 0.0001f ? imageUp.normalized : Vector3.up;
        }

        private static bool AllPointsInside(Camera camera, Vector3[] points, float margin)
        {
            float safe = Mathf.Clamp(margin, 0.01f, 0.45f);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = camera.WorldToViewportPoint(points[i]);
                if (p.z <= camera.nearClipPlane || p.x < safe || p.x > 1f - safe || p.y < safe || p.y > 1f - safe) return false;
            }
            return true;
        }

        private static bool HasBlockingGeometry(SubjectPoints subject, Vector3 cameraPosition, out string blockerName)
        {
            blockerName = string.Empty;
            Vector3 direction = cameraPosition - subject.Center;
            float distance = direction.magnitude;
            if (distance <= 0.01f) { blockerName = "camera too close"; return true; }
            RaycastHit[] hits = Physics.RaycastAll(subject.Center, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hit = hits[i].transform;
                if (hit == null || hit.IsChildOf(subject.Female.transform)) continue;
                blockerName = hit.name;
                return true;
            }
            return false;
        }

        private IEnumerator TweenCamera(Vector3 fromPosition, Quaternion fromRotation, float fromFov, Vector3 toPosition, Quaternion toRotation, float toFov, float duration)
        {
            if (duration <= 0f)
            {
                SetOverride(toPosition, toRotation, toFov);
                yield break;
            }
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / duration);
                float eased = t * t * (3f - 2f * t);
                SetOverride(Vector3.Lerp(fromPosition, toPosition, eased), Quaternion.Slerp(fromRotation, toRotation, eased), Mathf.Lerp(fromFov, toFov, eased));
                if (t >= 1f) break;
                yield return null;
            }
        }

        private void SetOverride(Vector3 position, Quaternion rotation, float fov)
        {
            _overridePosition = position;
            _overrideRotation = rotation;
            _overrideFov = fov;
            ApplyCameraOverride();
        }

        private string CapturePng(Camera camera)
        {
            int width = _cfgCaptureWidth.Value;
            int height = _cfgCaptureHeight.Value;
            string relative = string.IsNullOrWhiteSpace(_cfgOutputDirectory.Value) ? "captures" : _cfgOutputDirectory.Value.Trim();
            string outputDir = Path.IsPathRooted(relative) ? relative : Path.Combine(PluginDir, relative);
            Directory.CreateDirectory(outputDir);
            string path = Path.Combine(outputDir, "portrait_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            Texture2D texture = null;
            try
            {
                camera.targetTexture = rt;
                camera.aspect = width / (float)height;
                camera.Render();
                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path;
            }
            finally
            {
                camera.aspect = previousAspect;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (texture != null) UnityEngine.Object.Destroy(texture);
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
