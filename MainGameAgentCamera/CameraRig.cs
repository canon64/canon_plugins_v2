using System;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// ゲームのカメラへの読み書きを一手に引き受ける。
    ///
    /// 書き込みは CameraControl_Ver2 の CameraData に対して行う。
    /// Transform を直接殴るとゲーム側の更新に毎フレーム上書きされて喧嘩になるが、
    /// CameraData に入れればゲーム自身がその構図を維持してくれるうえ、
    /// 人間がマウスで続きを操作することもできる。
    ///
    /// CameraData の意味 (BaseCameraControl_Ver2.CameraUpdate より):
    ///   rotation = transBase.rotation * Euler(Rot)
    ///   position = rotation * Dir + transBase.TransformPoint(Pos)
    /// つまり Pos=注視点(ローカル)、Dir=注視点からカメラへのオフセット(カメラローカル)、Rot=角度。
    /// </summary>
    internal static class CameraRig
    {
        // ゲーム側の可動制限を外した時の元の値。restore で戻すために覚えておく。
        private static bool _limitsSuspended;
        private static bool _savedLimitPos;
        private static bool _savedLimitDir;
        private static bool _savedLimitRot;

        private static HFlag _cachedHFlag;
        private static int _searchFrame = -1;
        private static CameraControl_Ver2 _cachedCtrl;

        internal static CameraControl_Ver2 Resolve()
        {
            if (_cachedHFlag == null && _searchFrame != Time.frameCount)
            {
                _searchFrame = Time.frameCount;
                _cachedHFlag = UnityEngine.Object.FindObjectOfType<HFlag>();
            }

            if (_cachedHFlag != null)
            {
                CameraControl_Ver2 live = _cachedHFlag.ctrlCamera;
                if (live != null) _cachedCtrl = live;
            }

            if (_cachedCtrl == null)
                _cachedCtrl = UnityEngine.Object.FindObjectOfType<CameraControl_Ver2>();

            return _cachedCtrl != null ? _cachedCtrl : null;
        }

        internal static Camera ResolveCamera(CameraControl_Ver2 ctrl)
        {
            if (ctrl != null && ctrl.thisCmaera != null) return ctrl.thisCmaera;
            return Camera.main;
        }

        internal static Vector3 WorldLookAt(CameraControl_Ver2 ctrl)
        {
            BaseCameraControl_Ver2.CameraData d = ctrl.GetCameraData();
            return ctrl.transBase != null ? ctrl.transBase.TransformPoint(d.Pos) : d.Pos;
        }

        internal static Vector3 WorldPosition(CameraControl_Ver2 ctrl)
        {
            Camera cam = ResolveCamera(ctrl);
            if (cam != null) return cam.transform.position;

            BaseCameraControl_Ver2.CameraData d = ctrl.GetCameraData();
            Quaternion rot = ctrl.transBase != null
                ? ctrl.transBase.rotation * Quaternion.Euler(d.Rot)
                : Quaternion.Euler(d.Rot);
            return rot * d.Dir + WorldLookAt(ctrl);
        }

        /// <summary>
        /// 注視点・カメラ位置・ロール・画角から構図を作って書き込む。
        /// 角度計算はすべてワールドで済ませ、最後だけコントローラのローカルへ畳む。
        /// </summary>
        internal static void ApplyFraming(
            CameraControl_Ver2 ctrl,
            Vector3 lookAt,
            Vector3 cameraPosition,
            Vector3 up,
            float roll,
            float fov)
        {
            Vector3 forward = lookAt - cameraPosition;
            if (forward.sqrMagnitude < 0.000001f) forward = Vector3.forward;

            Vector3 safeUp = up;
            if (safeUp.sqrMagnitude < 0.000001f || Mathf.Abs(Vector3.Dot(safeUp.normalized, forward.normalized)) > 0.999f)
                safeUp = Vector3.up;

            Quaternion worldRot = Quaternion.LookRotation(forward.normalized, safeUp.normalized);
            if (Mathf.Abs(roll) > 0.001f)
                worldRot = worldRot * Quaternion.Euler(0f, 0f, roll);

            // 制限が生きていると、指定した注視点が毎フレーム基準点へ引き戻され、
            // 狙った部位より下(基準点寄り)を写す絵になる。書き込む前に必ず外す。
            SuspendLimits(ctrl);

            BaseCameraControl_Ver2.CameraData data = ctrl.GetCameraData();
            if (ctrl.transBase != null)
            {
                data.Pos = ctrl.transBase.InverseTransformPoint(lookAt);
                data.Rot = NormalizeEuler((Quaternion.Inverse(ctrl.transBase.rotation) * worldRot).eulerAngles);
            }
            else
            {
                data.Pos = lookAt;
                data.Rot = NormalizeEuler(worldRot.eulerAngles);
            }

            data.Dir = Quaternion.Inverse(worldRot) * (cameraPosition - lookAt);
            data.Fov = Mathf.Clamp(fov, 5f, 120f);

            ctrl.SetCameraData(data);
            ApplyFov(ctrl, data.Fov);
        }

        /// <summary>
        /// 注視点・距離・角度の可動制限を外す。
        ///
        /// CameraUpdate() が毎フレーム Pos を limitPos(既定2m) の球内へ、
        /// Dir.z と Rot を範囲内へ押し戻すため、外部から構図を指定しても
        /// そのままでは通らない。エージェントが構図を作る間だけ解除する。
        /// </summary>
        internal static void SuspendLimits(CameraControl_Ver2 ctrl)
        {
            if (ctrl == null || _limitsSuspended) return;
            _savedLimitPos = ctrl.isLimitPos;
            _savedLimitDir = ctrl.isLimitDir;
            _savedLimitRot = ctrl.isLlimitRot;
            _limitsSuspended = true;
            ctrl.isLimitPos = false;
            ctrl.isLimitDir = false;
            ctrl.isLlimitRot = false;
        }

        /// <summary>外した制限を元に戻す。</summary>
        internal static void RestoreLimits(CameraControl_Ver2 ctrl)
        {
            if (ctrl == null || !_limitsSuspended) return;
            ctrl.isLimitPos = _savedLimitPos;
            ctrl.isLimitDir = _savedLimitDir;
            ctrl.isLlimitRot = _savedLimitRot;
            _limitsSuspended = false;
        }

        internal static bool LimitsSuspended => _limitsSuspended;

        internal static void ApplyFov(CameraControl_Ver2 ctrl, float fov)
        {
            float clamped = Mathf.Clamp(fov, 5f, 120f);
            ctrl.CameraFov = clamped;
            ctrl.CameraInitFov = clamped;
            Camera cam = ResolveCamera(ctrl);
            if (cam != null && !cam.orthographic) cam.fieldOfView = clamped;

            // MainGameCameraControl が毎フレーム自分のFOVを再適用するため、
            // ここに直接書いただけでは画面上は元に戻される。
            // 向こうが用意している公開APIを通すと、一定時間その値が保持される。
            NotifyExternalFov(clamped);
        }

        private static bool _externalFovResolved;
        private static System.Reflection.MethodInfo _externalFovMethod;

        /// <summary>
        /// MainGameCameraControl へ「この画角を使え」と伝える。
        /// 依存を増やしたくないので反射で呼ぶ。相手が居なければ黙って諦める。
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

            if (_externalFovMethod == null) return;
            try { _externalFovMethod.Invoke(null, new object[] { fov, "MainGameAgentCamera" }); }
            catch { }
        }

        /// <summary>
        /// 体基準の球面角からカメラ位置を作る。
        /// yaw=0 は体の正面。+yaw は被写体から見て右へ回り込む。+pitch は見下ろし。
        /// </summary>
        internal static Vector3 OrbitPosition(
            Vector3 lookAt, Vector3 bodyUp, Vector3 bodyFront, float yaw, float pitch, float distance)
        {
            Vector3 dir = Quaternion.AngleAxis(yaw, bodyUp) * bodyFront.normalized;
            Vector3 right = Vector3.Cross(bodyUp, dir).normalized;
            if (right.sqrMagnitude < 0.000001f) right = Vector3.right;
            dir = Quaternion.AngleAxis(-pitch, right) * dir;
            return lookAt + dir.normalized * Mathf.Max(0.05f, distance);
        }

        /// <summary>現在の構図を体基準の yaw/pitch/距離へ戻す。nudge の起点に使う。</summary>
        internal static void DecomposeOrbit(
            Vector3 lookAt, Vector3 cameraPosition, Vector3 bodyUp, Vector3 bodyFront,
            out float yaw, out float pitch, out float distance)
        {
            Vector3 offset = cameraPosition - lookAt;
            distance = offset.magnitude;
            if (distance < 0.0001f)
            {
                yaw = 0f;
                pitch = 0f;
                return;
            }

            Vector3 dir = offset / distance;
            Vector3 up = bodyUp.normalized;
            Vector3 front = bodyFront.normalized;
            Vector3 right = Vector3.Cross(up, front).normalized;

            float vertical = Vector3.Dot(dir, up);
            pitch = Mathf.Asin(Mathf.Clamp(vertical, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 flat = dir - up * vertical;
            if (flat.sqrMagnitude < 0.000001f)
            {
                yaw = 0f;
                return;
            }
            flat.Normalize();
            yaw = Mathf.Atan2(Vector3.Dot(flat, right), Vector3.Dot(flat, front)) * Mathf.Rad2Deg;
        }

        internal static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        // ---- カメラ入力が誰かに止められていないかの確認 ----

        internal static bool IsMouseControlBlocked(CameraControl_Ver2 ctrl)
        {
            return Evaluate(ctrl != null ? ctrl.NoCtrlCondition : null, false);
        }

        internal static bool IsKeyControlBlocked(CameraControl_Ver2 ctrl)
        {
            if (ctrl == null || ctrl.KeyCondition == null) return false;
            return !Evaluate(ctrl.KeyCondition, true);
        }

        private static bool Evaluate(BaseCameraControl_Ver2.NoCtrlFunc func, bool fallback)
        {
            if (func == null) return fallback;
            try { return func(); }
            catch { return fallback; }
        }

        /// <summary>止めている delegate の正体を名前で返す。犯人特定用。</summary>
        internal static string DescribeBlocker(CameraControl_Ver2 ctrl)
        {
            if (ctrl == null) return "no controller";
            return Describe(ctrl.NoCtrlCondition) + " / key=" + Describe(ctrl.KeyCondition);
        }

        private static string Describe(BaseCameraControl_Ver2.NoCtrlFunc func)
        {
            if (func == null) return "null";
            try
            {
                System.Reflection.MethodInfo m = func.Method;
                string type = m.DeclaringType != null ? m.DeclaringType.FullName : "?";
                return type + "." + m.Name;
            }
            catch { return "<broken>"; }
        }
    }
}
