using MainGameTransformGizmo;
using UnityEngine;
using VRGIN.Core;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private GameObject _desktopGizmoProxy;
        private TransformGizmo _desktopGizmo;

        private bool IsDesktopGizmoDragging()
        {
            return !VR.Active && _desktopGizmo != null && _desktopGizmo.IsDragging;
        }

        private void DrawDesktopGizmoControls()
        {
            Section("デスクトップ操作");
            Toggle("ギズモ表示", ref _settings.ShowDesktopGizmo);
            if (_model == null)
            {
                GUILayout.Label("ディルドを呼び出すと移動・回転ギズモを操作できます");
                return;
            }
            if (VR.Active)
            {
                GUILayout.Label("VR中はグリップ操作を使用します");
                return;
            }
            if (_desktopGizmo == null) return;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("移動")) _desktopGizmo.SetMode(GizmoMode.Move);
            if (GUILayout.Button("回転")) _desktopGizmo.SetMode(GizmoMode.Rotate);
            GUILayout.EndHorizontal();
            GUILayout.Label("ギズモの軸をドラッグして位置・向きを変更");
        }

        private void UpdateDesktopGizmo()
        {
            if (_model == null || _settings == null || !_settings.ShowDesktopGizmo || VR.Active ||
                !TransformGizmoApi.IsAvailable)
            {
                DetachDesktopGizmo();
                return;
            }
            if (_desktopGizmo == null)
            {
                _desktopGizmoProxy = new GameObject("__VRVibeDesktopGizmo");
                _desktopGizmoProxy.hideFlags = HideFlags.HideAndDontSave;
                _desktopGizmoProxy.transform.position = _model.transform.position;
                _desktopGizmoProxy.transform.rotation = _model.transform.rotation;
                _desktopGizmo = TransformGizmoApi.Attach(_desktopGizmoProxy);
                if (_desktopGizmo == null)
                {
                    DetachDesktopGizmo();
                    return;
                }
                TransformGizmoApi.EnableCameraInputCaptureOnDrag(_desktopGizmo);
                TransformGizmoApi.SetSizeMultiplier(_desktopGizmo, 0.35f);
                _desktopGizmo.SetMode(GizmoMode.Move);
                _desktopGizmo.SetVisible(true);
            }
            if (_desktopGizmo.Mode == GizmoMode.Scale) _desktopGizmo.SetMode(GizmoMode.Move);
            if (_desktopGizmo.IsDragging)
            {
                _model.transform.position = _desktopGizmoProxy.transform.position;
                _model.transform.rotation = _desktopGizmoProxy.transform.rotation;
            }
            else
            {
                _desktopGizmoProxy.transform.position = _model.transform.position;
                _desktopGizmoProxy.transform.rotation = _model.transform.rotation;
            }
        }

        private void DetachDesktopGizmo()
        {
            if (_desktopGizmo != null)
                TransformGizmoApi.DisableCameraInputCaptureOnDrag(_desktopGizmo);
            if (_desktopGizmoProxy != null)
            {
                TransformGizmoApi.Detach(_desktopGizmoProxy);
                Destroy(_desktopGizmoProxy);
            }
            _desktopGizmo = null;
            _desktopGizmoProxy = null;
        }
    }
}
