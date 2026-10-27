using System;
using MainGameTransformGizmo;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        /// <summary>
        /// 右クリックの瞬間にギズモ状態をログ出力する診断用。
        /// </summary>
        private void DebugLogRightClickIfAny()
        {
            if (!_settings.DebugLogEnabled) return;
            if (!GetGameMouseButtonDown(1)) return;

            Camera cam = Camera.main;
            Vector3 mouse = Input.mousePosition;
            string camInfo = cam != null
                ? "cam=" + cam.name + " px=" + cam.pixelWidth + "x" + cam.pixelHeight
                : "cam=NULL";

            if (_selectedGizmo == null)
            {
                LogInfo("[DEBUG-RCLICK] mouse=" + ((Vector2)mouse).ToString("F1") + " " + camInfo
                    + " gizmo=NULL ownerId=" + (_selectedGizmoOwnerId ?? "<null>"));
                return;
            }

            Vector3 gzPos = _selectedGizmo.transform.position;
            float distPx = -1f;
            bool behind = false;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(gzPos);
                behind = sp.z < 0f;
                distPx = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(mouse.x, mouse.y));
            }

            LogInfo("[DEBUG-RCLICK] mouse=" + ((Vector2)mouse).ToString("F1") + " " + camInfo
                + " gizmo.pos=" + gzPos.ToString("F3")
                + " visible=" + _selectedGizmo.IsVisible
                + " axis=" + _selectedGizmo.AxisSpace
                + " mode=" + _selectedGizmo.Mode
                + " distPx=" + distPx.ToString("F1")
                + " behindCam=" + behind);
        }

        private void UpdateSelectedGizmoBinding()
        {
            if (!_settings.EnableSelectedGizmo)
            {
                DetachSelectedGizmo();
                return;
            }

            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                DetachSelectedGizmo();
                return;
            }

            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef == null || runtimeRef.GameObject == null)
            {
                DetachSelectedGizmo();
                return;
            }

            // 同じ選択先なら再付け替えしない（可視だけ確保）
            if (string.Equals(_selectedGizmoOwnerId, selected.id, StringComparison.Ordinal)
                && _selectedGizmo != null && _selectedGizmoProxy != null)
            {
                _selectedGizmo.SetVisible(true);
                return;
            }

            DetachSelectedGizmo();

            // Proxy GameObject を生成（scale=1 固定）。これにギズモを付ける。
            _selectedGizmoProxy = new GameObject("__ObjComposerGizmoProxy_" + selected.id);
            _selectedGizmoProxy.hideFlags = HideFlags.HideAndDontSave;
            Transform realT = runtimeRef.GameObject.transform;
            _selectedGizmoProxy.transform.SetParent(null, false);
            _selectedGizmoProxy.transform.position = realT.position;
            _selectedGizmoProxy.transform.rotation = realT.rotation;
            _selectedGizmoProxy.transform.localScale = Vector3.one;

            _selectedGizmoOwnerId = selected.id;
            _selectedGizmo = TransformGizmoApi.Attach(_selectedGizmoProxy);
            if (_selectedGizmo == null)
            {
                UnityEngine.Object.Destroy(_selectedGizmoProxy);
                _selectedGizmoProxy = null;
                _selectedGizmoOwnerId = null;
                return;
            }

            _selectedGizmo.DragStateChanged += OnSelectedGizmoDragStateChanged;
            _selectedGizmo.SetVisible(true);
            _selectedGizmo.SetSizeMultiplier(_settings.GizmoSizeMultiplier);
            // Scale モードは Composer 側のスライダーがあるので無効化（HipHijack と同じ作法）
            if (_selectedGizmo.Mode == GizmoMode.Scale) _selectedGizmo.SetMode(GizmoMode.Move);
            _selectedGizmoUndoCaptured = false;
        }

        private void DetachSelectedGizmo()
        {
            NotifyGizmoDragState(false);

            if (_selectedGizmo != null)
            {
                try
                {
                    _selectedGizmo.DragStateChanged -= OnSelectedGizmoDragStateChanged;
                }
                catch { /* ignore */ }
            }
            if (_selectedGizmoProxy != null)
            {
                TransformGizmoApi.Detach(_selectedGizmoProxy);
                UnityEngine.Object.Destroy(_selectedGizmoProxy);
                _selectedGizmoProxy = null;
            }

            _selectedGizmo = null;
            _selectedGizmoOwnerId = null;
            _selectedGizmoUndoCaptured = false;
        }

        private void OnSelectedGizmoDragStateChanged(bool dragging)
        {
            NotifyGizmoDragState(dragging);

            if (dragging)
            {
                if (!_selectedGizmoUndoCaptured)
                {
                    RecordUndoSnapshot("gizmo drag");
                    _selectedGizmoUndoCaptured = true;
                }
                return;
            }

            if (_selectedGizmoUndoCaptured)
            {
                _selectedGizmoUndoCaptured = false;
                SyncSelectedFromGizmo();
                SaveLayoutIfNeeded();
                RefreshSelectedEditorFields();
            }
        }

        private void TickSelectedGizmoSync()
        {
            if (_selectedGizmo == null || _selectedGizmoProxy == null) return;

            ManagedObjectData selected = GetSelectedData();
            if (selected == null || !string.Equals(selected.id, _selectedGizmoOwnerId, StringComparison.Ordinal))
            {
                DetachSelectedGizmo();
                return;
            }

            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef == null || runtimeRef.GameObject == null)
            {
                DetachSelectedGizmo();
                return;
            }

            _selectedGizmo.SetVisible(_settings.EnableSelectedGizmo);
            _selectedGizmo.SetSizeMultiplier(_settings.GizmoSizeMultiplier);
            if (_selectedGizmo.Mode == GizmoMode.Scale) _selectedGizmo.SetMode(GizmoMode.Move);

            Transform realT = runtimeRef.GameObject.transform;
            Transform proxyT = _selectedGizmoProxy.transform;
            if (!proxyT.localScale.Equals(Vector3.one))
                proxyT.localScale = Vector3.one; // Proxy は常にスケール 1

            if (_selectedGizmo.IsDragging)
            {
                // ギズモが Proxy を動かした → 本オブジェクトに反映
                realT.position = proxyT.position;
                realT.rotation = proxyT.rotation;
                SyncDataFromRuntime(runtimeRef);
                RefreshSelectedEditorFields();
            }
            else
            {
                // 本オブジェクトの位置/回転（他のロジックで変わり得る）を Proxy に同期
                Vector3 position = realT.position;
                Quaternion rotation = realT.rotation;
                if (!proxyT.position.Equals(position)) proxyT.position = position;
                if (!proxyT.rotation.Equals(rotation)) proxyT.rotation = rotation;
            }
        }

        private void SyncSelectedFromGizmo()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null) return;

            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef == null) return;

            SyncDataFromRuntime(runtimeRef);
        }
    }
}
