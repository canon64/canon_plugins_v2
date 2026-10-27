using UnityEngine;
using MainGameTransformGizmo;
namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  private TransformGizmo _actorGizmo;
  private bool _actorGizmoRequested;
  private Vector3 _actorGizmoPosition;
  private Quaternion _actorGizmoRotation;
  public static bool GetActorGizmoVisible() => Instance != null && Instance._actorGizmo != null && Instance._actorGizmo.IsVisible;
  private void ToggleActorGizmo() {
   if (!CanChangeActorPlace) return;
   _actorGizmoRequested = !_actorGizmoRequested;
   TickActorGizmo();
  }
  private void TickActorGizmo() {
   FlushActorPlaceChanges();
   bool show = _actorGizmoRequested && _cfgEnabled.Value && CanChangeActorPlace;
   if (show && _actorGizmo == null) {
    if (!TransformGizmoApi.TryAttach(_actorStage, out _actorGizmo)) { _actorPlacementStatus = "ギズモ機能が利用できません"; _actorGizmoRequested = false; return; }
    _actorGizmo.SetSizeMultiplier(0.25f);
    _actorGizmo.CenterDragOnXZPlane = true;
    _actorGizmo.SetCenterColor(new Color(0.3f,0.9f,1f));
    _actorGizmo.ModeChanged += mode => { if (mode == GizmoMode.Scale) _actorGizmo.SetMode(GizmoMode.Move); };
    _actorGizmo.DragStateChanged += dragging => {
     if (dragging || !CanChangeActorPlace) return;
     ApplyActorPlacement(_actorStage.transform.position, _actorStage.transform.rotation);
     RefreshActorPlacementText();
     AutoSaveActorPlace(); FlushActorPlaceChanges(true);
    };
    TransformGizmoApi.EnableCameraInputCaptureOnDrag(_actorGizmo);
    _actorGizmoPosition = _actorStage.transform.position;
    _actorGizmoRotation = _actorStage.transform.rotation;
   }
   if (_actorGizmo == null) return;
   _actorGizmo.SetVisible(show);
   if (show && (_actorGizmoPosition != _actorStage.transform.position || _actorGizmoRotation != _actorStage.transform.rotation)) {
    _actorGizmoPosition = _actorStage.transform.position; _actorGizmoRotation = _actorStage.transform.rotation;
    ApplyActorPlacement(_actorGizmoPosition, _actorGizmoRotation);
    RefreshActorPlacementText();
    AutoSaveActorPlace();
   }
  }
  private void ReleaseActorGizmo() {
   FlushActorPlaceChanges(true);
   _actorGizmoRequested = false;
   if (_actorGizmo != null) {
    _actorGizmo.SetVisible(false);
    TransformGizmoApi.DisableCameraInputCaptureOnDrag(_actorGizmo);
    Destroy(_actorGizmo); _actorGizmo = null;
   }
  }
 }
}
