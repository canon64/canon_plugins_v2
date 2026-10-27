using MainGameTransformGizmo;
using MainGameUnifiedUi;
using UnityEngine;
using VRGIN.Core;

namespace MainGameVrNeckChain
{
    public sealed partial class Plugin
    {
        private const string ChainTabId = "vr-neck-chain";
        private GameObject _desktopGizmoProxy;
        private TransformGizmo _desktopGizmo;
        private bool _desktopGizmoEnabled;
        private GameObject _rootGizmoProxy;
        private TransformGizmo _rootGizmo;
        private bool _rootGizmoEnabled;
        private GameObject _collarGizmoProxy;
        private TransformGizmo _collarGizmo;
        private bool _collarGizmoEnabled;

        private void RegisterChainTab()
        {
            if (UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting(ChainTabId))
                UnifiedUiApi.RegisterTab(ChainTabId, "鎖", DrawChainTab, 81);
        }

        private void DrawChainTab()
        {
            GUILayout.Label("Leash Choker + Chain Short Weighted / 通常Hの最初の女性に自動装着");
            if (GUILayout.Button(_chain == null ? "首輪と鎖を呼び出す" : "首輪と鎖を削除する", GUILayout.Width(190f)))
            {
                ToggleSpawn();
            }
            GUILayout.Label(_chain == null
                ? (_scene == null ? "状態: 通常Hシーン待ち" : "状態: 鎖なし")
                : "状態: 首輪" + (_collarAttached ? "装着" : "取り外し") +
                  " / 鎖" + (_rootAttached ? "接続" : "未接続") +
                  " / 先端" + (_grabController != null || _desktopGizmoProxy != null ? "保持中" : "自由"));

            GUILayout.Space(5f);
            GUILayout.Label("デスクトップ操作");
            bool original = GUI.enabled;
            GUI.enabled = original && _chain != null && !VR.Active && TransformGizmoApi.IsAvailable;
            if (GUILayout.Button(_desktopGizmoEnabled ? "先端を離す" : "先端ギズモを表示・保持", GUILayout.Width(230f)))
            {
                _desktopGizmoEnabled = !_desktopGizmoEnabled;
                if (!_desktopGizmoEnabled) DetachDesktopGizmo();
                else UpdateDesktopGizmo();
            }
            GUI.enabled = original && _chain != null && !_rootAttached && !VR.Active && TransformGizmoApi.IsAvailable;
            if (GUILayout.Button(_rootGizmoEnabled ? "接続端を離す" : "接続端ギズモを表示・保持", GUILayout.Width(230f)))
            {
                _rootGizmoEnabled = !_rootGizmoEnabled;
                if (!_rootGizmoEnabled) DetachRootGizmo(); else UpdateDesktopGizmo();
            }
            GUI.enabled = original && _collar != null && !VR.Active && TransformGizmoApi.IsAvailable;
            if (GUILayout.Button(_collarGizmoEnabled ? "首輪を離す" : "首輪ギズモを表示・保持", GUILayout.Width(230f)))
            {
                _collarGizmoEnabled = !_collarGizmoEnabled;
                if (!_collarGizmoEnabled) { DetachCollarGizmo(); TryAttachCollar(); }
                else UpdateCollarGizmo();
            }
            GUI.enabled = original;
            if (_rootAttached && GUILayout.Button("鎖の接続部分を外す", GUILayout.Width(230f)))
            {
                DetachRoot();
                if (!VR.Active) { _rootGizmoEnabled = true; UpdateRootGizmo(); }
            }
            GUILayout.Label("ギズモで両端と首輪を移動。接続端を首輪のリング近くで離すと、その位置で接続します。");

            GUILayout.Space(5f);
            GUILayout.Label("首輪（a_n_neck 骨へ装着）");
            DrawConfigSlider("大きさ（全体）", _collarScale, 0.6f, 2f);
            DrawConfigSlider("大きさ X", _collarScaleX, 0.3f, 3f);
            DrawConfigSlider("大きさ Y", _collarScaleY, 0.3f, 3f);
            DrawConfigSlider("大きさ Z", _collarScaleZ, 0.3f, 3f);
            DrawConfigSlider("首輪 左右 m", _collarOffsetX, -0.12f, 0.12f);
            DrawConfigSlider("首輪 上下 m", _collarOffsetY, -0.12f, 0.12f);
            DrawConfigSlider("首輪 前後 m", _collarOffsetZ, -0.12f, 0.12f);
            DrawConfigSlider("首輪 回転 X°", _collarRotationX, -180f, 180f);
            DrawConfigSlider("首輪 回転 Y°", _collarRotationY, -180f, 180f);
            DrawConfigSlider("首輪 回転 Z°", _collarRotationZ, -180f, 180f);
            DrawConfigSlider("首輪が戻る距離 m", _collarAttachRadius, 0.02f, 0.5f);
            DrawConfigSlider("首輪が外れる距離 m", _collarDetachRadius, 0.03f, 1f);
            GUILayout.Label("首輪のリングと鎖の接続位置");
            DrawConfigSlider("リング 左右 m", _ringOffsetX, -0.12f, 0.12f);
            DrawConfigSlider("リング 上下 m", _ringOffsetY, -0.12f, 0.12f);
            DrawConfigSlider("リング 前後 m", _ringOffsetZ, -0.12f, 0.12f);
            DrawConfigSlider("鎖 左右微調整 m", _chainOffsetX, -0.15f, 0.15f);
            DrawConfigSlider("鎖 上下微調整 m", _neckDown, -0.25f, 0.15f);
            DrawConfigSlider("鎖 前後微調整 m", _neckForward, -0.15f, 0.25f);
            DrawConfigSlider("鎖の大きさ（全体）", _chainScale, 0.5f, 2f);
            DrawConfigSlider("鎖の大きさ X", _chainScaleX, 0.3f, 3f);
            DrawConfigSlider("鎖の大きさ Y", _chainScaleY, 0.3f, 3f);
            DrawConfigSlider("鎖の大きさ Z", _chainScaleZ, 0.3f, 3f);
            DrawConfigSlider("鎖の接続距離 m", _chainAttachRadius, 0.02f, 0.3f);
            DrawColliderControls();
            GUILayout.Label("VR操作");
            DrawConfigSlider("掴む距離 m", _grabRadius, 0.05f, 0.5f);
            GUILayout.Label("先端・接続端・首輪へ手を近づけてグリップ。首輪を引くと鎖も一緒に外れます。");
            if (GUILayout.Button("首輪・接続位置・掴む距離を初期値へ戻す", GUILayout.Width(300f)))
            {
                _collarScale.Value = 1f;
                _collarScaleX.Value = 1f;
                _collarScaleY.Value = 1f;
                _collarScaleZ.Value = 1f;
                _collarOffsetX.Value = 0f;
                _collarOffsetY.Value = 0f;
                _collarOffsetZ.Value = 0f;
                _collarRotationX.Value = 0f;
                _collarRotationY.Value = 0f;
                _collarRotationZ.Value = 0f;
                _ringOffsetX.Value = 0.055f;
                _ringOffsetY.Value = -0.03f;
                _ringOffsetZ.Value = 0f;
                _chainOffsetX.Value = 0f;
                _neckDown.Value = NeckOffset.y;
                _neckForward.Value = NeckOffset.z;
                _chainScale.Value = 1f;
                _chainScaleX.Value = 1f;
                _chainScaleY.Value = 1f;
                _chainScaleZ.Value = 1f;
                _grabRadius.Value = GrabRadius;
                _collarAttachRadius.Value = 0.10f;
                _collarDetachRadius.Value = 0.20f;
                _chainAttachRadius.Value = 0.08f;
            }
        }

        private static void DrawConfigSlider(string label, BepInEx.Configuration.ConfigEntry<float> setting,
            float minimum, float maximum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130f));
            float value = GUILayout.HorizontalSlider(setting.Value, minimum, maximum, GUILayout.Width(250f));
            if (!Mathf.Approximately(value, setting.Value)) setting.Value = value;
            GUILayout.Label(setting.Value.ToString("F3"), GUILayout.Width(55f));
            GUILayout.EndHorizontal();
        }

        private void DrawColliderControls()
        {
            GUILayout.Space(7f);
            GUILayout.Label("鎖と体のコライダー（青=補助、桃=既存の胸からコピー）");
            bool show = GUILayout.Toggle(_showColliderPreviews.Value, "コライダー表示");
            if (show != _showColliderPreviews.Value) _showColliderPreviews.Value = show;
            GUILayout.Label("使用中: " + _bodyCollisions.Count + " 個");
            if (_bodyCollisions.Count == 0) return;
            _selectedBodyCollision = Mathf.Clamp(_selectedBodyCollision, 0, _bodyCollisions.Count - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(34f)))
                _selectedBodyCollision = (_selectedBodyCollision + _bodyCollisions.Count - 1) % _bodyCollisions.Count;
            GUILayout.Label((_selectedBodyCollision + 1) + " / " + _bodyCollisions.Count + "  " +
                _bodyCollisions[_selectedBodyCollision].Label);
            if (GUILayout.Button("▶", GUILayout.Width(34f)))
                _selectedBodyCollision = (_selectedBodyCollision + 1) % _bodyCollisions.Count;
            GUILayout.EndHorizontal();
            BodyCollisionEntry entry = _bodyCollisions[_selectedBodyCollision];
            DrawConfigSlider("位置 X m", entry.OffsetX, -0.3f, 0.3f);
            DrawConfigSlider("位置 Y m", entry.OffsetY, -0.3f, 0.3f);
            DrawConfigSlider("位置 Z m", entry.OffsetZ, -0.3f, 0.3f);
            DrawConfigSlider("回転 X°", entry.RotationX, -180f, 180f);
            DrawConfigSlider("回転 Y°", entry.RotationY, -180f, 180f);
            DrawConfigSlider("回転 Z°", entry.RotationZ, -180f, 180f);
            DrawConfigSlider("半径倍率", entry.RadiusScale, 0.2f, 3f);
            DrawConfigSlider("長さ倍率", entry.LengthScale, 0.2f, 3f);
            if (GUILayout.Button("選択中のコライダーを初期値へ戻す", GUILayout.Width(290f)))
            {
                entry.OffsetX.Value = entry.OffsetY.Value = entry.OffsetZ.Value = 0f;
                entry.RotationX.Value = entry.RotationY.Value = entry.RotationZ.Value = 0f;
                entry.RadiusScale.Value = entry.LengthScale.Value = 1f;
            }
        }

        private void UpdateDesktopGizmo()
        {
            UpdateRootGizmo();
            if (!_desktopGizmoEnabled || _chain == null || VR.Active || !TransformGizmoApi.IsAvailable)
            {
                DetachDesktopGizmo();
                return;
            }
            if (_desktopGizmo != null) return;
            _desktopGizmoProxy = new GameObject("__NeckChainTipGizmo");
            _desktopGizmoProxy.hideFlags = HideFlags.HideAndDontSave;
            _desktopGizmoProxy.transform.position = _bones[BoneCount - 1].position;
            _desktopGizmo = TransformGizmoApi.Attach(_desktopGizmoProxy);
            if (_desktopGizmo == null)
            {
                DetachDesktopGizmo();
                return;
            }
            TransformGizmoApi.EnableCameraInputCaptureOnDrag(_desktopGizmo);
            TransformGizmoApi.SetSizeMultiplier(_desktopGizmo, 0.2f);
            _desktopGizmo.SetMode(GizmoMode.Move);
            _desktopGizmo.SetVisible(true);
            for (int i = 0; i < BoneCount; i++)
                _points[i] = _previous[i] = _bones[i].position;
            _dynamicBone.enabled = false;
        }

        private void UpdateRootGizmo()
        {
            if (!_rootGizmoEnabled || _rootAttached || _chain == null || VR.Active || !TransformGizmoApi.IsAvailable)
            {
                DetachRootGizmo();
                return;
            }
            if (_rootGizmo != null) return;
            _rootGizmoProxy = new GameObject("__NeckChainRootGizmo");
            _rootGizmoProxy.hideFlags = HideFlags.HideAndDontSave;
            _rootGizmoProxy.transform.position = _bones[0].position;
            _rootGizmo = TransformGizmoApi.Attach(_rootGizmoProxy);
            if (_rootGizmo == null) { DetachRootGizmo(); return; }
            TransformGizmoApi.EnableCameraInputCaptureOnDrag(_rootGizmo);
            TransformGizmoApi.SetSizeMultiplier(_rootGizmo, 0.2f);
            _rootGizmo.SetMode(GizmoMode.Move);
            _rootGizmo.SetVisible(true);
            InitializeSimulation();
            _dynamicBone.enabled = false;
        }

        private void UpdateCollarGizmo()
        {
            if (!_collarGizmoEnabled || _collar == null || VR.Active || !TransformGizmoApi.IsAvailable)
            {
                DetachCollarGizmo();
                return;
            }
            if (_collarGizmo != null) return;
            _collarGizmoProxy = new GameObject("__NeckChainCollarGizmo");
            _collarGizmoProxy.hideFlags = HideFlags.HideAndDontSave;
            _collarGizmoProxy.transform.position = _collar.transform.position;
            _collarGizmo = TransformGizmoApi.Attach(_collarGizmoProxy);
            if (_collarGizmo == null) { DetachCollarGizmo(); return; }
            TransformGizmoApi.EnableCameraInputCaptureOnDrag(_collarGizmo);
            TransformGizmoApi.SetSizeMultiplier(_collarGizmo, 0.2f);
            _collarGizmo.SetMode(GizmoMode.Move);
            _collarGizmo.SetVisible(true);
        }

        private void DetachRootGizmo()
        {
            if (_rootGizmoProxy != null && !_rootAttached) _rootAttachPending = true;
            if (_rootGizmo != null) TransformGizmoApi.DisableCameraInputCaptureOnDrag(_rootGizmo);
            if (_rootGizmoProxy != null) { TransformGizmoApi.Detach(_rootGizmoProxy); Destroy(_rootGizmoProxy); }
            _rootGizmo = null; _rootGizmoProxy = null; _rootGizmoEnabled = false;
        }

        private void DetachCollarGizmo()
        {
            if (_collarGizmoProxy != null && !_collarAttached) _collarAttachPending = true;
            if (_collarGizmo != null) TransformGizmoApi.DisableCameraInputCaptureOnDrag(_collarGizmo);
            if (_collarGizmoProxy != null) { TransformGizmoApi.Detach(_collarGizmoProxy); Destroy(_collarGizmoProxy); }
            _collarGizmo = null; _collarGizmoProxy = null; _collarGizmoEnabled = false;
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
            _desktopGizmoEnabled = false;
        }

        private void OnDisable()
        {
            UnifiedUiApi.UnregisterTab(ChainTabId);
            ClearChain();
        }
    }
}
