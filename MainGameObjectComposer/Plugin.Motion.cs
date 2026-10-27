using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        // ── 時間源 ─────────────────────────────────────────────────
        // 将来 BPM 連動時に BeatSync の _videoRoomTimeSec に差し替え可能
        private double GetSyncTime()
        {
            return Time.unscaledTime;
        }

        // ── Base 姿勢キャプチャ ───────────────────────────────────
        private void EnsureBaseLocalPose(RuntimeObjectRef runtimeRef)
        {
            if (runtimeRef == null || runtimeRef.GameObject == null)
            {
                return;
            }
            if (runtimeRef.HasBaseLocalPose)
            {
                return;
            }
            Transform tf = runtimeRef.GameObject.transform;
            runtimeRef.BaseLocalPosition = tf.localPosition;
            runtimeRef.BaseLocalRotation = tf.localRotation;
            runtimeRef.HasBaseLocalPose = true;
        }

        internal void InvalidateBaseLocalPose(RuntimeObjectRef runtimeRef)
        {
            if (runtimeRef == null)
            {
                return;
            }
            runtimeRef.HasBaseLocalPose = false;
        }

        internal void InvalidateBaseLocalPoseById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            if (_runtimeObjects.TryGetValue(id, out RuntimeObjectRef r))
            {
                InvalidateBaseLocalPose(r);
            }
        }

        // ── 毎フレーム動き処理 ────────────────────────────────────
        private void TickMotions(float dt)
        {
            if (_runtimeObjects.Count == 0)
            {
                return;
            }

            double tSec = GetSyncTime();

            foreach (RuntimeObjectRef runtimeRef in _runtimeObjects.Values)
            {
                if (runtimeRef == null || runtimeRef.Data == null || runtimeRef.GameObject == null)
                {
                    continue;
                }

                ManagedObjectData data = runtimeRef.Data;
                Transform tf = runtimeRef.GameObject.transform;

                switch (data.motionMode)
                {
                    case 1: // 回転
                    {
                        if (dt <= 0f) break;
                        Vector3 axis = data.autoRotateAxis;
                        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
                        float speed = data.autoRotateSpeedDegPerSec;
                        Space space = data.autoRotateLocalSpace ? Space.Self : Space.World;
                        if (speed != 0f)
                            tf.Rotate(axis.normalized, speed * dt, space);
                        SyncDataFromRuntime(runtimeRef);
                        runtimeRef.HasBaseLocalPose = false;
                        break;
                    }
                    case 2: // アングル（ワイパー）
                    {
                        EnsureBaseLocalPose(runtimeRef);
                        Vector3 axis = data.angleAxis;
                        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
                        float phase = (float)(2.0 * System.Math.PI * (data.angleSpeedHz * tSec + data.anglePhaseTurns));
                        float deg = Mathf.Sin(phase) * data.angleAmplitudeDeg;
                        Quaternion swing = Quaternion.AngleAxis(deg, axis.normalized);
                        if (data.angleLocalSpace)
                        {
                            Quaternion desired = runtimeRef.BaseLocalRotation * swing;
                            if (!tf.localRotation.Equals(desired)) tf.localRotation = desired;
                        }
                        else
                        {
                            if (!tf.localRotation.Equals(runtimeRef.BaseLocalRotation))
                                tf.localRotation = runtimeRef.BaseLocalRotation;
                            if (deg != 0f) tf.Rotate(axis.normalized, deg, Space.World);
                        }
                        break;
                    }
                    case 3: // ピストン
                    {
                        EnsureBaseLocalPose(runtimeRef);
                        Vector3 axis = data.pistonAxis;
                        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
                        float phase = (float)(2.0 * System.Math.PI * (data.pistonSpeedHz * tSec + data.pistonPhaseTurns));
                        float offset = Mathf.Sin(phase) * data.pistonAmplitude;
                        Vector3 axisN = axis.normalized;
                        if (data.pistonLocalSpace)
                        {
                            Vector3 desired = runtimeRef.BaseLocalPosition + axisN * offset;
                            if (!tf.localPosition.Equals(desired)) tf.localPosition = desired;
                        }
                        else
                        {
                            Transform parent = tf.parent;
                            Vector3 localAxis = parent != null
                                ? parent.InverseTransformDirection(axisN)
                                : axisN;
                            Vector3 desired = runtimeRef.BaseLocalPosition + localAxis * offset;
                            if (!tf.localPosition.Equals(desired)) tf.localPosition = desired;
                        }
                        break;
                    }
                    default:
                        runtimeRef.HasBaseLocalPose = false;
                        break;
                }
            }
        }

        // ── 選択オブジェクト Setter ──────────────────────────────
        private void SetSelectedMotionMode(int mode)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("set motion mode failed: no selected object");
                return;
            }
            if (mode < 0 || mode > 3)
            {
                mode = 0;
            }
            if (selected.motionMode == mode)
            {
                return;
            }

            RecordUndoSnapshot("set motion mode");
            selected.motionMode = mode;
            selected.autoRotate = (mode == 1);
            InvalidateBaseLocalPoseById(selected.id);
            SaveLayoutIfNeeded();
        }

        private void SetSelectedAutoRotate(bool enabled, Vector3 axis, float speedDegPerSec, bool localSpace)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("set auto rotate failed: no selected object");
                return;
            }
            if (axis.sqrMagnitude < 1e-6f)
            {
                axis = Vector3.up;
            }

            RecordUndoSnapshot("set auto rotate");
            selected.autoRotate = enabled;
            selected.autoRotateAxis = axis;
            selected.autoRotateSpeedDegPerSec = speedDegPerSec;
            selected.autoRotateLocalSpace = localSpace;
            if (enabled)
            {
                selected.motionMode = 1;
            }
            else if (selected.motionMode == 1)
            {
                selected.motionMode = 0;
            }
            InvalidateBaseLocalPoseById(selected.id);
            SaveLayoutIfNeeded();
        }

        private void SetSelectedAngle(Vector3 axis, float amplitudeDeg, float speedHz, float phaseTurns, bool localSpace)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("set angle failed: no selected object");
                return;
            }
            if (axis.sqrMagnitude < 1e-6f)
            {
                axis = Vector3.up;
            }

            RecordUndoSnapshot("set angle");
            selected.angleAxis = axis;
            selected.angleAmplitudeDeg = Mathf.Clamp(amplitudeDeg, 0f, 180f);
            selected.angleSpeedHz = Mathf.Clamp(speedHz, 0.01f, 20f);
            selected.anglePhaseTurns = Mathf.Repeat(phaseTurns, 1f);
            selected.angleLocalSpace = localSpace;
            InvalidateBaseLocalPoseById(selected.id);
            SaveLayoutIfNeeded();
        }

        private void SetSelectedPiston(Vector3 axis, float amplitude, float speedHz, float phaseTurns, bool localSpace)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("set piston failed: no selected object");
                return;
            }
            if (axis.sqrMagnitude < 1e-6f)
            {
                axis = Vector3.forward;
            }

            RecordUndoSnapshot("set piston");
            selected.pistonAxis = axis;
            selected.pistonAmplitude = Mathf.Clamp(amplitude, 0f, 10f);
            selected.pistonSpeedHz = Mathf.Clamp(speedHz, 0.01f, 20f);
            selected.pistonPhaseTurns = Mathf.Repeat(phaseTurns, 1f);
            selected.pistonLocalSpace = localSpace;
            InvalidateBaseLocalPoseById(selected.id);
            SaveLayoutIfNeeded();
        }
    }
}
