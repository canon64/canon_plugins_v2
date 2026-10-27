using UnityEngine;

namespace MainGameTransformGizmo
{
    /// <summary>
    /// Public entry points for other plugins.
    /// </summary>
    public static class TransformGizmoApi
    {
        public static bool IsAvailable => Plugin.Instance != null;

        public static TransformGizmo Attach(GameObject target)
        {
            if (target == null) return null;

            TransformGizmo gizmo = target.GetComponent<TransformGizmo>();
            if (gizmo == null)
            {
                gizmo = target.AddComponent<TransformGizmo>();
            }

            return gizmo;
        }

        public static bool TryAttach(GameObject target, out TransformGizmo gizmo)
        {
            gizmo = null;
            if (!IsAvailable || target == null) return false;

            gizmo = Attach(target);
            return gizmo != null;
        }

        public static TransformGizmo Get(GameObject target)
        {
            return target == null ? null : target.GetComponent<TransformGizmo>();
        }

        public static bool Detach(GameObject target)
        {
            TransformGizmo gizmo = Get(target);
            if (gizmo == null) return false;

            Object.Destroy(gizmo);
            return true;
        }

        public static bool EnableCameraInputCaptureOnDrag(GameObject target)
        {
            TransformGizmo gizmo = Get(target);
            if (gizmo == null) return false;
            return EnableCameraInputCaptureOnDrag(gizmo);
        }

        public static bool EnableCameraInputCaptureOnDrag(TransformGizmo gizmo)
        {
            if (!IsAvailable || gizmo == null) return false;
            return GizmoInputCaptureRuntime.EnableFor(gizmo);
        }

        public static bool DisableCameraInputCaptureOnDrag(GameObject target)
        {
            TransformGizmo gizmo = Get(target);
            if (gizmo == null) return false;
            return DisableCameraInputCaptureOnDrag(gizmo);
        }

        public static bool DisableCameraInputCaptureOnDrag(TransformGizmo gizmo)
        {
            if (gizmo == null) return false;
            return GizmoInputCaptureRuntime.DisableFor(gizmo);
        }

        public static bool IsCameraInputCaptureOnDragEnabled(TransformGizmo gizmo)
        {
            if (gizmo == null) return false;
            return GizmoInputCaptureRuntime.IsEnabledFor(gizmo);
        }

        public static bool EnableCameraInputCapture(string ownerKey)
        {
            if (!IsAvailable) return false;
            return GizmoInputCaptureRuntime.SetManualCapture(ownerKey, enabled: true);
        }

        public static bool DisableCameraInputCapture(string ownerKey)
        {
            return GizmoInputCaptureRuntime.SetManualCapture(ownerKey, enabled: false);
        }

        public static bool IsCameraInputCaptureEnabled(string ownerKey)
        {
            return GizmoInputCaptureRuntime.IsManualCaptureEnabled(ownerKey);
        }

        public static float GetSizeMultiplier(TransformGizmo gizmo)
        {
            return gizmo != null ? gizmo.SizeMultiplier : TransformGizmo.DefaultSizeMultiplier;
        }

        public static float GetSizeMultiplier(GameObject target)
        {
            return GetSizeMultiplier(Get(target));
        }

        public static bool SetSizeMultiplier(TransformGizmo gizmo, float sizeMultiplier)
        {
            if (gizmo == null) return false;
            gizmo.SetSizeMultiplier(sizeMultiplier);
            return true;
        }

        public static bool SetSizeMultiplier(GameObject target, float sizeMultiplier)
        {
            TransformGizmo gizmo = Get(target);
            if (gizmo == null) return false;
            gizmo.SetSizeMultiplier(sizeMultiplier);
            return true;
        }

        public static GizmoAxisSpace GetAxisSpace(TransformGizmo gizmo)
        {
            return gizmo != null ? gizmo.AxisSpace : GizmoAxisSpace.Local;
        }

        public static GizmoAxisSpace GetAxisSpace(GameObject target)
        {
            return GetAxisSpace(Get(target));
        }

        public static bool SetAxisSpace(TransformGizmo gizmo, GizmoAxisSpace axisSpace)
        {
            if (gizmo == null) return false;
            gizmo.SetAxisSpace(axisSpace);
            return true;
        }

        public static bool SetAxisSpace(GameObject target, GizmoAxisSpace axisSpace)
        {
            TransformGizmo gizmo = Get(target);
            if (gizmo == null) return false;
            gizmo.SetAxisSpace(axisSpace);
            return true;
        }
    }
}
