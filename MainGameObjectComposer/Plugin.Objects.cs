using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private static readonly PrimitiveType[] CreatePrimitiveOptions =
        {
            PrimitiveType.Sphere,
            PrimitiveType.Cube,
            PrimitiveType.Capsule,
            PrimitiveType.Cylinder,
            PrimitiveType.Plane,
            PrimitiveType.Quad
        };

        private static readonly string[] CreatePrimitiveLabels =
        {
            "球体",
            "立方体",
            "カプセル",
            "円柱",
            "平面",
            "四角"
        };

        private static bool IsParentKind(string kind, string expected)
        {
            return string.Equals(kind, expected, StringComparison.Ordinal);
        }

        private static bool IsKnownParentKind(string kind)
        {
            return IsParentKind(kind, ParentKindRoot)
                || IsParentKind(kind, ParentKindManaged)
                || IsParentKind(kind, ParentKindExternal);
        }

        private ManagedObjectData FindDataById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null && string.Equals(_objects[i].id, id, StringComparison.Ordinal))
                {
                    return _objects[i];
                }
            }

            return null;
        }

        private RuntimeObjectRef FindRuntimeById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            _runtimeObjects.TryGetValue(id, out RuntimeObjectRef runtimeRef);
            return runtimeRef;
        }

        private ManagedObjectData GetSelectedData()
        {
            return FindDataById(_selectedId);
        }

        private bool TryGetManagedObjectIdByTransform(Transform tf, out string id)
        {
            id = null;
            if (tf == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, RuntimeObjectRef> kv in _runtimeObjects)
            {
                RuntimeObjectRef runtimeRef = kv.Value;
                if (runtimeRef == null || runtimeRef.GameObject == null)
                {
                    continue;
                }

                if (runtimeRef.GameObject.transform == tf)
                {
                    id = kv.Key;
                    return true;
                }
            }

            return false;
        }

        private bool EnsureRuntimeRoot()
        {
            if (_runtime.HSceneProc == null)
            {
                return false;
            }

            if (_runtime.Root != null)
            {
                return true;
            }

            _runtime.Root = new GameObject("__MainGameObjectComposerRoot");
            _runtime.Root.hideFlags = HideFlags.HideAndDontSave;
            _runtime.Root.transform.SetParent(null, false);
            _runtime.Root.transform.position = Vector3.zero;
            _runtime.Root.transform.rotation = Quaternion.identity;
            _runtime.Root.transform.localScale = Vector3.one;
            return true;
        }

        private void DestroyAllRuntimeObjects()
        {
            if (MotionTraceEnabled) TraceComposerEvent("RUNTIME_DESTROY", "count=" + _runtimeObjects.Count
                + " callers=" + new System.Diagnostics.StackTrace(1, false));
            ClearBetterPenetrationPending();
            _appliedMotionPositionOffsets.Clear();
            foreach (RuntimeObjectRef runtimeRef in _runtimeObjects.Values)
            {
                if (runtimeRef == null) continue;
                if (runtimeRef.GeneratedMesh != null) Destroy(runtimeRef.GeneratedMesh);
                if (runtimeRef.GeneratedMaterial != null) Destroy(runtimeRef.GeneratedMaterial);
                if (runtimeRef.GameObject != null) Destroy(runtimeRef.GameObject);
            }
            _runtimeObjects.Clear();

            if (_runtime.Root != null)
            {
                Destroy(_runtime.Root);
                _runtime.Root = null;
            }
        }
        private void RebuildAllRuntimeObjects()
        {
            if (MotionTraceEnabled) TraceComposerEvent("RUNTIME_REBUILD", "objects=" + _objects.Count);
            DetachSelectedGizmo();
            DestroyAllRuntimeObjects();
            if (!EnsureRuntimeRoot())
            {
                return;
            }

            Transform root = _runtime.Root.transform;

            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData data = _objects[i];
                if (data == null)
                {
                    continue;
                }

                NormalizeData(data);

                // Wrapper: 空オブジェクト (scale=1) で位置/回転/親子関係を担当
                GameObject wrapper = new GameObject(data.name);
                wrapper.hideFlags = HideFlags.HideAndDontSave;
                wrapper.transform.SetParent(root, false);
                wrapper.transform.localScale = Vector3.one;

                // Visual: Wrapper の子。mesh とユーザーが設定する scale を持つ
                GameObject visual;
                try
                {
                    visual = GameObject.CreatePrimitive(data.primitive);
                }
                catch
                {
                    data.primitive = PrimitiveType.Sphere;
                    visual = GameObject.CreatePrimitive(data.primitive);
                }

                visual.name = "Visual";
                visual.hideFlags = HideFlags.HideAndDontSave;
                Collider col = visual.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                visual.transform.SetParent(wrapper.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = data.localScale;

                var runtimeRef = new RuntimeObjectRef
                {
                    Data = data,
                    GameObject = wrapper,
                    Visual = visual
                };
                _runtimeObjects[data.id] = runtimeRef;

                if (data.isRotationObject)
                {
                    ApplyRotationObjectVisualSetup(runtimeRef);
                }
                else if (data.isPistonObject)
                {
                    ApplyPistonObjectVisualSetup(runtimeRef);
                }
                else if (data.isAngleObject)
                {
                    ApplyAngleObjectVisualSetup(runtimeRef);
                }
                else
                {
                    ApplyVisibility(runtimeRef);
                }
            }

            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData data = _objects[i];
                if (data == null)
                {
                    continue;
                }

                RuntimeObjectRef runtimeRef = FindRuntimeById(data.id);
                if (runtimeRef == null || runtimeRef.GameObject == null)
                {
                    continue;
                }

                Transform parent = ResolveConfiguredParentTransform(data, data.id, out string resolvedKind, out string resolvedRefId);
                data.parentKind = resolvedKind;
                data.parentRefId = resolvedRefId;
                data.parentId = IsParentKind(resolvedKind, ParentKindManaged) ? resolvedRefId : null;

                // Wrapper: 位置・回転だけ。scale は 1 固定
                runtimeRef.GameObject.transform.SetParent(parent != null ? parent : root, false);
                runtimeRef.GameObject.transform.localPosition = data.localPosition;
                runtimeRef.GameObject.transform.localEulerAngles = data.localEulerAngles;
                runtimeRef.GameObject.transform.localScale = Vector3.one;
                // Visual: ユーザーが設定した scale を適用
                if (runtimeRef.Visual != null)
                {
                    runtimeRef.Visual.transform.localScale = data.localScale;
                }
            }

            _selectionDirty = true;
            UpdateSelectedGizmoBinding();
        }

        private Transform ResolveConfiguredParentTransform(ManagedObjectData data, string selfId, out string resolvedKind, out string resolvedRefId)
        {
            resolvedKind = ParentKindRoot;
            resolvedRefId = null;

            Transform root = _runtime.Root != null ? _runtime.Root.transform : null;
            if (data == null)
            {
                return root;
            }

            string parentKind = data.parentKind;
            string parentRefId = data.parentRefId;

            if (string.IsNullOrEmpty(parentKind) && !string.IsNullOrEmpty(data.parentId))
            {
                parentKind = ParentKindManaged;
                parentRefId = data.parentId;
            }

            if (!IsKnownParentKind(parentKind))
            {
                parentKind = ParentKindRoot;
                parentRefId = null;
            }

            if (IsParentKind(parentKind, ParentKindManaged))
            {
                if (string.IsNullOrEmpty(parentRefId)
                    || string.Equals(parentRefId, selfId, StringComparison.Ordinal)
                    || WouldCreateCycle(selfId, parentRefId))
                {
                    return root;
                }

                RuntimeObjectRef parentRef = FindRuntimeById(parentRefId);
                if (parentRef != null && parentRef.GameObject != null)
                {
                    resolvedKind = ParentKindManaged;
                    resolvedRefId = parentRefId;
                    return parentRef.GameObject.transform;
                }

                return root;
            }

            if (IsParentKind(parentKind, ParentKindExternal))
            {
                Transform externalParent = ResolveExternalParentTransform(parentRefId);
                if (externalParent != null)
                {
                    resolvedKind = ParentKindExternal;
                    resolvedRefId = parentRefId;
                    return externalParent;
                }

                return root;
            }

            return root;
        }

        private void SyncAllDataFromRuntime()
        {
            foreach (RuntimeObjectRef runtimeRef in _runtimeObjects.Values)
            {
                SyncDataFromRuntime(runtimeRef);
            }
        }

        private void SyncDataFromRuntime(RuntimeObjectRef runtimeRef)
        {
            if (runtimeRef == null || runtimeRef.Data == null || runtimeRef.GameObject == null)
            {
                return;
            }

            Transform tf = runtimeRef.GameObject.transform; // Wrapper

            // 回転/ピストン/アングルドライバの子は毎フレーム transform.localPosition/Rotation が
            // 上書きされるため、ここで runtime → data に書き戻すと駆動座標が累積する。
            // → data.localPosition/localEulerAngles (ユーザーオフセット) を保持する。
            if (!IsParentDriverObject(runtimeRef.Data))
            {
                runtimeRef.Data.localPosition = tf.localPosition - GetAppliedMotionPositionOffset(runtimeRef.Data.id);
                runtimeRef.Data.localEulerAngles = tf.localEulerAngles;
            }
            // scale は Visual 側に持たせている
            if (runtimeRef.Visual != null)
            {
                runtimeRef.Data.localScale = runtimeRef.Visual.transform.localScale;
            }
            runtimeRef.Data.name = runtimeRef.GameObject.name;

            Transform root = _runtime.Root != null ? _runtime.Root.transform : null;
            Transform parent = tf.parent;
            if (parent == null || parent == root)
            {
                runtimeRef.Data.parentKind = ParentKindRoot;
                runtimeRef.Data.parentRefId = null;
                runtimeRef.Data.parentId = null;
                return;
            }

            if (TryGetManagedObjectIdByTransform(parent, out string managedParentId))
            {
                runtimeRef.Data.parentKind = ParentKindManaged;
                runtimeRef.Data.parentRefId = managedParentId;
                runtimeRef.Data.parentId = managedParentId;
                return;
            }

            if (TryGetExternalParentKeyByTransform(parent, out string externalKey))
            {
                runtimeRef.Data.parentKind = ParentKindExternal;
                runtimeRef.Data.parentRefId = externalKey;
                runtimeRef.Data.parentId = null;
                return;
            }

            runtimeRef.Data.parentKind = ParentKindRoot;
            runtimeRef.Data.parentRefId = null;
            runtimeRef.Data.parentId = null;
        }

        private void NormalizeData(ManagedObjectData data)
        {
            if (data == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(data.id))
            {
                data.id = System.Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrEmpty(data.name))
            {
                data.name = "Object";
            }

            data.localScale.x = Mathf.Max(0.001f, data.localScale.x);
            data.localScale.y = Mathf.Max(0.001f, data.localScale.y);
            data.localScale.z = Mathf.Max(0.001f, data.localScale.z);

            if (data.autoRotateAxis.sqrMagnitude < 1e-6f)
            {
                data.autoRotateAxis = Vector3.up;
            }

            // 旧データ互換: motionMode 未設定で autoRotate=true なら回転モード(1)に移行
            if (data.motionMode == 0 && data.autoRotate)
            {
                data.motionMode = 1;
            }
            if (data.motionMode < 0 || data.motionMode > 3)
            {
                data.motionMode = 0;
            }

            if (data.angleAxis.sqrMagnitude < 1e-6f)
            {
                data.angleAxis = Vector3.up;
            }
            data.angleAmplitudeDeg = Mathf.Clamp(data.angleAmplitudeDeg, 0f, 180f);
            data.angleSpeedHz = Mathf.Clamp(data.angleSpeedHz, 0.01f, 20f);
            data.anglePhaseTurns = Mathf.Repeat(data.anglePhaseTurns, 1f);

            if (data.pistonAxis.sqrMagnitude < 1e-6f)
            {
                data.pistonAxis = Vector3.forward;
            }
            data.pistonAmplitude = Mathf.Clamp(data.pistonAmplitude, 0f, 10f);
            data.pistonSpeedHz = Mathf.Clamp(data.pistonSpeedHz, 0.01f, 20f);
            data.pistonPhaseTurns = Mathf.Repeat(data.pistonPhaseTurns, 1f);

            // ドライバ種別の排他保証（rotation > piston > angle）
            int driverCount = (data.isRotationObject ? 1 : 0)
                            + (data.isPistonObject ? 1 : 0)
                            + (data.isAngleObject ? 1 : 0);
            if (driverCount > 1)
            {
                if (data.isRotationObject) { data.isPistonObject = false; data.isAngleObject = false; }
                else if (data.isPistonObject) { data.isAngleObject = false; }
            }

            // ピストンドライバ形状パラメータのクランプ
            data.pistonRodRadius = Mathf.Clamp(data.pistonRodRadius, 0.0005f, 0.1f);

            // アングルドライバ形状パラメータのクランプ
            data.angleFanRadius = Mathf.Clamp(data.angleFanRadius, 0.01f, 2f);

            // ドライバ子の個別位相クランプ
            data.orbitPhaseTurns = Mathf.Repeat(data.orbitPhaseTurns, 1f);
            data.vibeSlowParentWeight = Mathf.Clamp01(data.vibeSlowParentWeight);
            data.vibeSlowRiseSeconds = Mathf.Clamp(data.vibeSlowRiseSeconds, 0.05f, 5f);
            data.vibeSlowFallSeconds = Mathf.Clamp(data.vibeSlowFallSeconds, 0.05f, 5f);

            if (string.IsNullOrEmpty(data.parentKind) && !string.IsNullOrEmpty(data.parentId))
            {
                data.parentKind = ParentKindManaged;
                data.parentRefId = data.parentId;
            }

            if (!IsKnownParentKind(data.parentKind))
            {
                data.parentKind = ParentKindRoot;
                data.parentRefId = null;
            }

            if (IsParentKind(data.parentKind, ParentKindRoot))
            {
                data.parentRefId = null;
                data.parentId = null;
                return;
            }

            if (IsParentKind(data.parentKind, ParentKindManaged))
            {
                if (string.IsNullOrEmpty(data.parentRefId))
                {
                    data.parentRefId = data.parentId;
                }

                if (string.IsNullOrEmpty(data.parentRefId))
                {
                    data.parentKind = ParentKindRoot;
                    data.parentRefId = null;
                    data.parentId = null;
                    return;
                }

                data.parentId = data.parentRefId;
                return;
            }

            if (string.IsNullOrEmpty(data.parentRefId))
            {
                data.parentKind = ParentKindRoot;
                data.parentRefId = null;
                data.parentId = null;
            }
            else
            {
                data.parentId = null;
            }
        }
        private void CreateObject(bool asChildOfSelected)
        {
            RecordUndoSnapshot("create object");

            PrimitiveType primitive = CreatePrimitiveOptions[Mathf.Clamp(_createPrimitiveIndex, 0, CreatePrimitiveOptions.Length - 1)];
            string id = System.Guid.NewGuid().ToString("N");
            string baseName = primitive.ToString();

            string parentKind = ParentKindRoot;
            string parentRefId = null;
            Vector3 localPosition = Vector3.zero;
            if (asChildOfSelected && !string.IsNullOrEmpty(_selectedId) && FindDataById(_selectedId) != null)
            {
                parentKind = ParentKindManaged;
                parentRefId = _selectedId;
                localPosition = _settings.DefaultChildOffset;
            }
            else if (TryGetCameraFrontWorldPos(_settings.DefaultSpawnDistance, out Vector3 spawnWorld))
            {
                Transform rootTf = (_runtime != null && _runtime.Root != null) ? _runtime.Root.transform : null;
                localPosition = WorldToManagedLocalPosition(spawnWorld, rootTf);
            }

            var data = new ManagedObjectData
            {
                id = id,
                name = BuildUniqueObjectName(baseName),
                primitive = primitive,
                parentKind = parentKind,
                parentRefId = parentRefId,
                parentId = IsParentKind(parentKind, ParentKindManaged) ? parentRefId : null,
                localPosition = localPosition,
                localEulerAngles = Vector3.zero,
                localScale = _settings.DefaultScale,
                autoRotate = false,
                autoRotateAxis = _settings.DefaultAutoRotateAxis,
                autoRotateSpeedDegPerSec = _settings.DefaultAutoRotateSpeedDegPerSec,
                autoRotateLocalSpace = _settings.DefaultAutoRotateLocalSpace
            };

            NormalizeData(data);
            _objects.Add(data);

            _selectedId = data.id;
            _selectionDirty = true;

            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("created object id=" + data.id + " name=" + data.name + " primitive=" + data.primitive);
        }

        private void CreateChildSphereForSelected()
        {
            if (GetSelectedData() == null)
            {
                LogWarn("create child sphere failed: no selected object");
                return;
            }

            RecordUndoSnapshot("create child sphere");

            var data = new ManagedObjectData
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = BuildUniqueObjectName("ChildSphere"),
                primitive = PrimitiveType.Sphere,
                parentKind = ParentKindManaged,
                parentRefId = _selectedId,
                parentId = _selectedId,
                localPosition = _settings.DefaultChildOffset,
                localEulerAngles = Vector3.zero,
                localScale = _settings.DefaultScale,
                autoRotate = false,
                autoRotateAxis = _settings.DefaultAutoRotateAxis,
                autoRotateSpeedDegPerSec = _settings.DefaultAutoRotateSpeedDegPerSec,
                autoRotateLocalSpace = _settings.DefaultAutoRotateLocalSpace
            };

            NormalizeData(data);
            _objects.Add(data);

            _selectedId = data.id;
            _selectionDirty = true;

            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("created child sphere id=" + data.id);
        }

        private string BuildUniqueObjectName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Object";
            }

            int next = 1;
            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData data = _objects[i];
                if (data == null || string.IsNullOrEmpty(data.name))
                {
                    continue;
                }

                if (!data.name.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string suffix = data.name.Substring(baseName.Length + 1);
                if (int.TryParse(suffix, out int parsed) && parsed >= next)
                {
                    next = parsed + 1;
                }
            }

            return baseName + "_" + next.ToString("000");
        }

        private void DeleteSelectedObject()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("delete failed: no selected object");
                return;
            }

            RecordUndoSnapshot("delete object");

            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData data = _objects[i];
                if (data == null)
                {
                    continue;
                }

                if (IsParentKind(data.parentKind, ParentKindManaged)
                    && string.Equals(data.parentRefId, selected.id, StringComparison.Ordinal))
                {
                    data.parentKind = selected.parentKind;
                    data.parentRefId = selected.parentRefId;
                    data.parentId = IsParentKind(data.parentKind, ParentKindManaged) ? data.parentRefId : null;
                }
            }

            _objects.Remove(selected);

            if (IsParentKind(_parentCandidateKind, ParentKindManaged)
                && string.Equals(_parentCandidateRefId, selected.id, StringComparison.Ordinal))
            {
                SetParentCandidateRoot();
            }

            if (string.Equals(_selectedGizmoOwnerId, selected.id, StringComparison.Ordinal))
            {
                DetachSelectedGizmo();
            }

            _selectedId = _objects.Count > 0 ? _objects[0].id : null;
            _selectionDirty = true;

            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("deleted object id=" + selected.id);
        }

        private void ClearAllObjects()
        {
            if (_objects.Count == 0)
            {
                return;
            }

            RecordUndoSnapshot("clear all objects");

            _objects.Clear();
            _selectedId = null;
            SetParentCandidateRoot();
            _selectionDirty = true;

            DetachSelectedGizmo();
            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("cleared all objects");
        }

        private void SetParentCandidateRoot()
        {
            _parentCandidateKind = ParentKindRoot;
            _parentCandidateRefId = null;
        }

        private void SetParentCandidateManaged(string id)
        {
            if (string.IsNullOrEmpty(id) || FindDataById(id) == null)
            {
                SetParentCandidateRoot();
                return;
            }

            _parentCandidateKind = ParentKindManaged;
            _parentCandidateRefId = id;
        }

        private void SetParentCandidateExternal(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                SetParentCandidateRoot();
                return;
            }

            _parentCandidateKind = ParentKindExternal;
            _parentCandidateRefId = key;
        }

        private void ApplyParentCandidateToSelection()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("set parent failed: no selected object");
                return;
            }

            string candidateKind = _parentCandidateKind;
            string candidateRefId = _parentCandidateRefId;
            if (string.IsNullOrEmpty(candidateKind) || IsParentKind(candidateKind, ParentKindRoot))
            {
                candidateKind = ParentKindRoot;
                candidateRefId = null;
            }
            else if (IsParentKind(candidateKind, ParentKindManaged))
            {
                if (string.IsNullOrEmpty(candidateRefId) || FindDataById(candidateRefId) == null)
                {
                    LogWarn("set parent failed: parent candidate is missing");
                    return;
                }

                if (string.Equals(selected.id, candidateRefId, StringComparison.Ordinal))
                {
                    LogWarn("set parent failed: cannot parent to itself");
                    return;
                }

                if (WouldCreateCycle(selected.id, candidateRefId))
                {
                    LogWarn("set parent failed: cycle detected");
                    return;
                }
            }
            else if (IsParentKind(candidateKind, ParentKindExternal))
            {
                if (ResolveExternalParentTransform(candidateRefId) == null)
                {
                    LogWarn("set parent failed: external target missing");
                    return;
                }
            }
            else
            {
                LogWarn("set parent failed: invalid parent kind");
                return;
            }

            if (string.Equals(selected.parentKind, candidateKind, StringComparison.Ordinal)
                && string.Equals(selected.parentRefId, candidateRefId, StringComparison.Ordinal))
            {
                return;
            }

            RecordUndoSnapshot("set parent");
            selected.parentKind = candidateKind;
            selected.parentRefId = candidateRefId;
            selected.parentId = IsParentKind(candidateKind, ParentKindManaged) ? candidateRefId : null;

            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            LogInfo("parent set child=" + selected.id + " parent=" + GetParentDisplayLabel(candidateKind, candidateRefId));
        }

        private void ClearParentOfSelection()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("clear parent failed: no selected object");
                return;
            }

            if (IsParentKind(selected.parentKind, ParentKindRoot))
            {
                return;
            }

            RecordUndoSnapshot("clear parent");
            selected.parentKind = ParentKindRoot;
            selected.parentRefId = null;
            selected.parentId = null;

            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            LogInfo("parent cleared id=" + selected.id);
        }
        private bool WouldCreateCycle(string childId, string newParentId)
        {
            if (string.IsNullOrEmpty(childId) || string.IsNullOrEmpty(newParentId))
            {
                return false;
            }

            string current = newParentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(current) && guard < 1024)
            {
                if (string.Equals(current, childId, StringComparison.Ordinal))
                {
                    return true;
                }

                ManagedObjectData data = FindDataById(current);
                if (data == null || !IsParentKind(data.parentKind, ParentKindManaged))
                {
                    break;
                }

                current = data.parentRefId;
                guard++;
            }

            return false;
        }

        private void RenameSelectedObject(string newName)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                return;
            }

            string trimmed = string.IsNullOrWhiteSpace(newName) ? selected.name : newName.Trim();
            if (string.Equals(selected.name, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            RecordUndoSnapshot("rename object");
            selected.name = trimmed;
            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef != null && runtimeRef.GameObject != null)
            {
                runtimeRef.GameObject.name = trimmed;
            }

            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
        }

        private void SetSelectedTransform(Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("set transform failed: no selected object");
                return;
            }

            localScale.x = Mathf.Max(0.001f, localScale.x);
            localScale.y = Mathf.Max(0.001f, localScale.y);
            localScale.z = Mathf.Max(0.001f, localScale.z);

            RecordUndoSnapshot("set transform");

            selected.localPosition = localPosition;
            selected.localEulerAngles = localEulerAngles;
            selected.localScale = localScale;

            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef != null && runtimeRef.GameObject != null)
            {
                Transform tf = runtimeRef.GameObject.transform;
                tf.localPosition = localPosition;
                tf.localEulerAngles = localEulerAngles;
                tf.localScale = localScale;
                SyncDataFromRuntime(runtimeRef);
                InvalidateBaseLocalPose(runtimeRef);
            }

            SaveLayoutIfNeeded();
        }

        // 動き系 Setter（SetSelectedMotionMode / SetSelectedAutoRotate / SetSelectedAngle / SetSelectedPiston）
        // は Plugin.Motion.cs を参照

        private void NudgeSelectedPosition(Vector3 delta)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                return;
            }

            SetSelectedTransform(
                selected.localPosition + delta,
                selected.localEulerAngles,
                selected.localScale);
        }

        private void NudgeSelectedRotation(Vector3 deltaEuler)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                return;
            }

            SetSelectedTransform(
                selected.localPosition,
                selected.localEulerAngles + deltaEuler,
                selected.localScale);
        }

        private void NudgeSelectedScale(Vector3 delta)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                return;
            }

            SetSelectedTransform(
                selected.localPosition,
                selected.localEulerAngles,
                selected.localScale + delta);
        }

        private void FlattenSelectedToDisc()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                return;
            }

            Vector3 nextScale = selected.localScale;
            nextScale.y = Mathf.Max(0.001f, _settings.DiskFlattenYScale);
            SetSelectedTransform(selected.localPosition, selected.localEulerAngles, nextScale);
        }

        private void MoveSelectedToMidpoint()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                LogWarn("中間点移動失敗: 選択オブジェクトなし");
                return;
            }

            RuntimeObjectRef runtimeRef = FindRuntimeById(selected.id);
            if (runtimeRef == null || runtimeRef.GameObject == null)
            {
                LogWarn("中間点移動失敗: ランタイム参照なし");
                return;
            }

            Transform selectedTransform = runtimeRef.GameObject.transform;
            Vector3 selectedWorld = selectedTransform.position;

            if (!TryResolveMidpointTarget(selectedTransform, selectedWorld, out Vector3 targetWorld, out string anchorName))
            {
                LogWarn("中間点移動失敗: 親/候補/体位基準点が見つかりません");
                return;
            }

            Vector3 nextLocal = selectedTransform.parent != null
                ? selectedTransform.parent.InverseTransformPoint(targetWorld)
                : targetWorld;

            SetSelectedTransform(nextLocal, selectedTransform.localEulerAngles, selectedTransform.localScale);
            RefreshSelectedEditorFields();
            LogInfo("中間点移動: anchor=" + anchorName + " pos=" + FormatVec3(targetWorld));
        }

        private bool TryResolveMidpointTarget(Transform selectedTransform, Vector3 selectedWorld, out Vector3 targetWorld, out string anchorName)
        {
            targetWorld = Vector3.zero;
            anchorName = string.Empty;

            if (selectedTransform != null && selectedTransform.parent != null)
            {
                Transform root = _runtime.Root != null ? _runtime.Root.transform : null;
                if (selectedTransform.parent != root)
                {
                    anchorName = "現在の親";
                    targetWorld = (selectedWorld + selectedTransform.parent.position) * 0.5f;
                    return true;
                }
            }

            Transform candidate = ResolveParentCandidateTransform();
            if (candidate != null)
            {
                anchorName = "親候補";
                targetWorld = (selectedWorld + candidate.position) * 0.5f;
                return true;
            }

            if (_runtime.MainFemale != null && TryGetNowHPointPosition(out Vector3 hpointPos))
            {
                anchorName = "女キャラ/体位ポイント";
                targetWorld = (_runtime.MainFemale.transform.position + hpointPos) * 0.5f;
                return true;
            }

            return false;
        }

        private Transform ResolveParentCandidateTransform()
        {
            if (IsParentKind(_parentCandidateKind, ParentKindManaged))
            {
                RuntimeObjectRef runtimeRef = FindRuntimeById(_parentCandidateRefId);
                return runtimeRef != null && runtimeRef.GameObject != null ? runtimeRef.GameObject.transform : null;
            }

            if (IsParentKind(_parentCandidateKind, ParentKindExternal))
            {
                return ResolveExternalParentTransform(_parentCandidateRefId);
            }

            return null;
        }

        private string GetParentDisplayLabel(string kind, string refId)
        {
            if (string.IsNullOrEmpty(kind) || IsParentKind(kind, ParentKindRoot))
            {
                return "ルート";
            }

            if (IsParentKind(kind, ParentKindManaged))
            {
                ManagedObjectData data = FindDataById(refId);
                if (data != null)
                {
                    return "管理:" + data.name;
                }

                return "管理:" + (string.IsNullOrEmpty(refId) ? "<none>" : refId);
            }

            if (IsParentKind(kind, ParentKindExternal))
            {
                if (TryGetExternalParentTarget(refId, out ExternalParentTarget target))
                {
                    return target.Category + ":" + target.Label;
                }

                return "外部:" + (string.IsNullOrEmpty(refId) ? "<none>" : refId);
            }

            return "不明";
        }

        private string GetParentDisplayLabel(ManagedObjectData data)
        {
            if (data == null)
            {
                return "<none>";
            }

            return GetParentDisplayLabel(data.parentKind, data.parentRefId);
        }

        private string GetParentCandidateDisplayLabel()
        {
            return GetParentDisplayLabel(_parentCandidateKind, _parentCandidateRefId);
        }

        private void SaveLayoutIfNeeded()
        {
            if (_movementEditPreset != null) return;
            if (_settings.AutoSaveOnMutation)
            {
                SaveLayout();
            }
        }

        private void CreateCylinder()
        {
            int prev = _createPrimitiveIndex;
            _createPrimitiveIndex = 3; // PrimitiveType.Cylinder
            CreateObject(asChildOfSelected: false);
            _createPrimitiveIndex = prev;
        }

        /// <summary>
        /// data の親が何らかのドライバ（回転/ピストン/アングル）かを判定する。
        /// Tick系が localPosition/Rotation を毎フレーム上書きする子の識別に使う。
        /// </summary>
        private bool IsParentDriverObject(ManagedObjectData data)
        {
            if (data == null) return false;
            if (!IsParentKind(data.parentKind, ParentKindManaged)) return false;
            if (string.IsNullOrEmpty(data.parentRefId)) return false;
            ManagedObjectData parent = FindDataById(data.parentRefId);
            return parent != null
                && (parent.isRotationObject || parent.isPistonObject || parent.isAngleObject);
        }

        /// <summary>
        /// target が ancestor の子孫（または同一）かを判定。サイクル防止用。
        /// </summary>
        private bool IsDescendantOrSelf(string targetId, string ancestorId)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(ancestorId)) return false;
            if (string.Equals(targetId, ancestorId, StringComparison.Ordinal)) return true;

            string cursor = targetId;
            for (int safety = 0; safety < 1024; safety++)
            {
                ManagedObjectData d = FindDataById(cursor);
                if (d == null) return false;
                if (!IsParentKind(d.parentKind, ParentKindManaged)) return false;
                if (string.Equals(d.parentRefId, ancestorId, StringComparison.Ordinal)) return true;
                cursor = d.parentRefId;
                if (string.IsNullOrEmpty(cursor)) return false;
            }
            return false;
        }

        /// <summary>
        /// targetId のオブジェクトを「現在選択中のオブジェクト」の子にする。
        /// 親が回転オブジェクトなら localPosition=0 にスナップ（軌道に乗る）。
        /// それ以外なら世界座標を維持（飛ばない）。
        /// </summary>
        private void SetObjectAsChildOfSelected(string targetId)
        {
            if (string.IsNullOrEmpty(_selectedId))
            {
                LogWarn("set as child failed: no selection");
                return;
            }
            if (string.Equals(_selectedId, targetId, StringComparison.Ordinal))
            {
                LogWarn("set as child failed: cannot parent to self");
                return;
            }
            if (IsDescendantOrSelf(_selectedId, targetId))
            {
                LogWarn("set as child failed: cycle (selected is descendant of target)");
                return;
            }

            ManagedObjectData target = FindDataById(targetId);
            ManagedObjectData parent = FindDataById(_selectedId);
            if (target == null || parent == null) return;

            RecordUndoSnapshot("set as child of selected");

            RuntimeObjectRef targetRrChild = FindRuntimeById(targetId);
            RuntimeObjectRef parentRr = FindRuntimeById(_selectedId);
            Transform oldParent = targetRrChild != null && targetRrChild.GameObject != null
                ? targetRrChild.GameObject.transform.parent
                : (_runtime != null && _runtime.Root != null ? _runtime.Root.transform : null);
            Transform newParent = parentRr != null && parentRr.GameObject != null ? parentRr.GameObject.transform : null;

            if (parent.isRotationObject)
            {
                // 軌道に乗せる: オフセット (0,0,0) からスタート → スライダーは初期 0
                target.localPosition = Vector3.zero;
                target.localEulerAngles = Vector3.zero;
                target.parentKind = ParentKindManaged;
                target.parentRefId = _selectedId;
                target.parentId = _selectedId;
                target.orientToTangent = parent.orientChildrenToTangent;

                // アクティブプリセットは「軌道に吸い付く」ので、L1/L2 の値も (0,0,0) でゼロ化
                if (_activePreset != null && newParent != null)
                {
                    ZeroOutPresetEntryPosition(targetId, ParentKindManaged, _selectedId);
                }
            }
            else
            {
                // アクティブプリセットの L1/L2 を新しい親基準に再投影
                if (newParent != null)
                {
                    ReprojectActivePresetForObject(targetId, oldParent, newParent, ParentKindManaged, _selectedId);
                }

                if (targetRrChild != null && targetRrChild.GameObject != null && newParent != null)
                {
                    Transform tt = targetRrChild.GameObject.transform;
                    Vector3 worldPos = tt.position;
                    Quaternion worldRot = tt.rotation;
                    target.localPosition = newParent.InverseTransformPoint(worldPos);
                    target.localEulerAngles = (Quaternion.Inverse(newParent.rotation) * worldRot).eulerAngles;
                }

                target.parentKind = ParentKindManaged;
                target.parentRefId = _selectedId;
                target.parentId = _selectedId;
            }

            RebuildAllRuntimeObjects();
            if (_activePreset != null) RebuildActivePresetCache();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("reparented id=" + targetId + " -> parent=" + _selectedId
                + " parentKind=" + (parent.isRotationObject ? "rotation(snap)" : "normal(preserveWorld)"));
        }

        /// <summary>
        /// アクティブプリセットの L1/L2 の指定オブジェクトの localPosition/Euler を (0,0,0) にゼロ化し、
        /// parentKind/parentRefId を新しい値に更新する。回転オブジェクトの子に吸い付かせる用途。
        /// </summary>
        private void ZeroOutPresetEntryPosition(string objId, string newKind, string newRefId)
        {
            if (_activePreset == null || string.IsNullOrEmpty(objId)) return;
            bool m1 = ZeroOutObjectInJsonList(_activePreset.loop1Json, objId, newKind, newRefId);
            bool m2 = ZeroOutObjectInJsonList(_activePreset.loop2Json, objId, newKind, newRefId);
            if (m1 || m2) SavePresets();
        }

        private bool ZeroOutObjectInJsonList(List<string> jsonList, string objId, string newKind, string newRefId)
        {
            if (jsonList == null) return false;
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (string.IsNullOrEmpty(jsonList[i])) continue;
                ManagedObjectData d;
                try { d = UnityEngine.JsonUtility.FromJson<ManagedObjectData>(jsonList[i]); }
                catch { continue; }
                if (d == null || !string.Equals(d.id, objId, StringComparison.Ordinal)) continue;
                d.localPosition = Vector3.zero;
                d.localEulerAngles = Vector3.zero;
                d.parentKind = newKind;
                d.parentRefId = newRefId;
                d.parentId = string.Equals(newKind, ParentKindManaged, StringComparison.Ordinal) ? newRefId : null;
                jsonList[i] = UnityEngine.JsonUtility.ToJson(d, false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// targetId のオブジェクトを外部親 (キャラ本体や FK/IK ボーン) の子にする。
        /// 世界位置を保つよう localPosition/localEulerAngles を再計算する。
        /// アクティブプリセット中なら、L1/L2 の保存値も新しい親基準に再投影する。
        /// </summary>
        private void SetObjectParentToExternal(string targetId, string externalKey)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(externalKey)) return;
            ManagedObjectData target = FindDataById(targetId);
            if (target == null) return;
            if (!TryGetExternalParentTarget(externalKey, out ExternalParentTarget extTarget) || extTarget == null || extTarget.Transform == null)
            {
                LogWarn("set external parent failed: target key not available = " + externalKey);
                return;
            }

            RecordUndoSnapshot("set external parent");

            RuntimeObjectRef targetRr = FindRuntimeById(targetId);
            Transform oldParent = targetRr != null && targetRr.GameObject != null
                ? targetRr.GameObject.transform.parent
                : (_runtime != null && _runtime.Root != null ? _runtime.Root.transform : null);

            // アクティブプリセットの L1/L2 を新しい親基準に再投影
            ReprojectActivePresetForObject(targetId, oldParent, extTarget.Transform, ParentKindExternal, externalKey);

            // 世界位置を保つよう localPosition / localEulerAngles を再計算
            if (targetRr != null && targetRr.GameObject != null)
            {
                Transform tt = targetRr.GameObject.transform;
                Vector3 worldPos = tt.position;
                Quaternion worldRot = tt.rotation;
                Vector3 newLocalPos = extTarget.Transform.InverseTransformPoint(worldPos);
                Quaternion newLocalRot = Quaternion.Inverse(extTarget.Transform.rotation) * worldRot;
                target.localPosition = newLocalPos;
                target.localEulerAngles = newLocalRot.eulerAngles;
            }

            target.parentKind = ParentKindExternal;
            target.parentRefId = externalKey;
            target.parentId = null;

            RebuildAllRuntimeObjects();
            if (_activePreset != null) RebuildActivePresetCache();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("reparented id=" + targetId + " -> external=" + externalKey);
        }

        /// <summary>
        /// アクティブプリセットの L1/L2 に含まれる「objId」エントリの localPosition/localEulerAngles を
        /// 旧親 oldParent 基準 → 新親 newParent 基準 に再投影し、parentKind/parentRefId も更新する。
        /// 世界位置を保ったまま親を付け替えるため。
        /// </summary>
        private void ReprojectActivePresetForObject(string objId, Transform oldParent, Transform newParent,
            string newKind, string newRefId)
        {
            if (_activePreset == null) return;
            if (oldParent == null || newParent == null) return;
            if (string.IsNullOrEmpty(objId)) return;

            bool m1 = ReprojectObjectInJsonList(_activePreset.loop1Json, objId, oldParent, newParent, newKind, newRefId);
            bool m2 = ReprojectObjectInJsonList(_activePreset.loop2Json, objId, oldParent, newParent, newKind, newRefId);
            if (m1 || m2) SavePresets();
        }

        private bool ReprojectObjectInJsonList(List<string> jsonList, string objId, Transform oldParent, Transform newParent,
            string newKind, string newRefId)
        {
            if (jsonList == null || jsonList.Count == 0) return false;
            bool modified = false;
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (string.IsNullOrEmpty(jsonList[i])) continue;
                ManagedObjectData d;
                try { d = UnityEngine.JsonUtility.FromJson<ManagedObjectData>(jsonList[i]); }
                catch { continue; }
                if (d == null) continue;
                if (!string.Equals(d.id, objId, StringComparison.Ordinal)) continue;

                Vector3 worldPos = oldParent.TransformPoint(d.localPosition);
                Quaternion worldRot = oldParent.rotation * Quaternion.Euler(d.localEulerAngles);
                d.localPosition = newParent.InverseTransformPoint(worldPos);
                d.localEulerAngles = (Quaternion.Inverse(newParent.rotation) * worldRot).eulerAngles;
                d.parentKind = newKind;
                d.parentRefId = newRefId;
                d.parentId = string.Equals(newKind, ParentKindManaged, StringComparison.Ordinal) ? newRefId : null;
                jsonList[i] = UnityEngine.JsonUtility.ToJson(d, false);
                modified = true;
                break;
            }
            return modified;
        }

        /// <summary>
        /// targetId のオブジェクトをルート直下に戻す（親解除）。
        /// </summary>
        private void SetObjectAsRoot(string targetId)
        {
            ManagedObjectData target = FindDataById(targetId);
            if (target == null) return;
            if (IsParentKind(target.parentKind, ParentKindRoot)) return;

            RecordUndoSnapshot("detach to root");

            RuntimeObjectRef targetRr = FindRuntimeById(targetId);
            Transform oldParent = targetRr != null && targetRr.GameObject != null
                ? targetRr.GameObject.transform.parent
                : null;
            Transform rootTf = _runtime != null && _runtime.Root != null ? _runtime.Root.transform : null;

            // アクティブプリセットの L1/L2 を root 基準に再投影
            if (oldParent != null && rootTf != null)
            {
                ReprojectActivePresetForObject(targetId, oldParent, rootTf, ParentKindRoot, null);
            }

            if (targetRr != null && targetRr.GameObject != null && rootTf != null)
            {
                Transform tt = targetRr.GameObject.transform;
                Vector3 worldPos = tt.position;
                Quaternion worldRot = tt.rotation;
                target.localPosition = rootTf.InverseTransformPoint(worldPos);
                target.localEulerAngles = (Quaternion.Inverse(rootTf.rotation) * worldRot).eulerAngles;
            }

            target.parentKind = ParentKindRoot;
            target.parentRefId = null;
            target.parentId = null;

            RebuildAllRuntimeObjects();
            if (_activePreset != null) RebuildActivePresetCache();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("detached id=" + targetId + " -> root");
        }

        private void ApplyTransformLive(Vector3 pos, Vector3 rot, Vector3 scale)
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null) return;

            Vector3 oldPos = selected.localPosition;
            Vector3 oldRot = selected.localEulerAngles;
            Vector3 oldScale = selected.localScale;

            selected.localPosition = pos;
            selected.localEulerAngles = rot;
            selected.localScale = scale;

            RuntimeObjectRef rr = FindRuntimeById(selected.id);
            if (rr == null || rr.GameObject == null) return;

            Vector3 motionOffset = GetEffectiveMotionPositionOffset(selected.id);
            rr.GameObject.transform.localPosition = pos + motionOffset;
            SetAppliedMotionPositionOffset(selected.id, motionOffset);
            rr.GameObject.transform.localEulerAngles = rot;
            if (rr.Visual != null)
            {
                rr.Visual.transform.localScale = scale;
            }
            InvalidateBaseLocalPose(rr);

            // アクティブプリセット中はスライダー編集の値が次フレームに補間で上書きされるので、
            // 編集 delta を L1/L2 両方に足し込んでプリセットを更新し、編集を維持する
            if (_activePreset != null)
            {
                ShiftActivePresetEntryDelta(selected.id, pos - oldPos, rot - oldRot, scale - oldScale);
            }
        }

        /// <summary>
        /// アクティブプリセットの L1/L2 に含まれる該当オブジェクトの transform に
        /// delta を加算する。スライダー編集を即座に反映するための関数。
        /// </summary>
        private void ShiftActivePresetEntryDelta(string objId, Vector3 dPos, Vector3 dRot, Vector3 dScale)
        {
            if (_activePreset == null || string.IsNullOrEmpty(objId)) return;
            // 変化が 0 なら何もしない（毎フレーム呼ばれる可能性があるので無駄を省く）
            if (dPos.sqrMagnitude < 1e-10f && dRot.sqrMagnitude < 1e-10f && dScale.sqrMagnitude < 1e-10f) return;

            bool m1 = ShiftDeltaInJsonList(_activePreset.loop1Json, objId, dPos, dRot, dScale);
            bool m2 = ShiftDeltaInJsonList(_activePreset.loop2Json, objId, dPos, dRot, dScale);
            if (m1 || m2)
            {
                SavePresets();
                RebuildActivePresetCache();
            }
        }

        private static bool ShiftDeltaInJsonList(List<string> jsonList, string objId, Vector3 dPos, Vector3 dRot, Vector3 dScale)
        {
            if (jsonList == null) return false;
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (string.IsNullOrEmpty(jsonList[i])) continue;
                ManagedObjectData d;
                try { d = UnityEngine.JsonUtility.FromJson<ManagedObjectData>(jsonList[i]); }
                catch { continue; }
                if (d == null || !string.Equals(d.id, objId, StringComparison.Ordinal)) continue;
                d.localPosition += dPos;
                d.localEulerAngles += dRot;
                d.localScale += dScale;
                jsonList[i] = UnityEngine.JsonUtility.ToJson(d, false);
                return true;
            }
            return false;
        }

        /// <summary>回転パラメータの編集差分を L1/L2 両方に保持する。</summary>
        private void SyncRotationParamsToActivePreset(string objId, ManagedObjectData before, ManagedObjectData after)
        {
            if (_activePreset == null || before == null || after == null) return;
            bool m1 = ShiftRotationParamsInJsonList(_activePreset.loop1Json, objId, before, after);
            bool m2 = ShiftRotationParamsInJsonList(_activePreset.loop2Json, objId, before, after);
            if (m1 || m2)
            {
                SavePresets();
                RebuildActivePresetCache();
            }
        }

        private static bool ShiftRotationParamsInJsonList(List<string> jsonList, string objId, ManagedObjectData before, ManagedObjectData after)
        {
            if (jsonList == null) return false;
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (string.IsNullOrEmpty(jsonList[i])) continue;
                ManagedObjectData d;
                try { d = UnityEngine.JsonUtility.FromJson<ManagedObjectData>(jsonList[i]); }
                catch { continue; }
                if (d == null || !string.Equals(d.id, objId, StringComparison.Ordinal)) continue;
                bool changed = false;
                changed |= ShiftFloat(ref d.orbitRadiusX, before.orbitRadiusX, after.orbitRadiusX);
                changed |= ShiftFloat(ref d.orbitRadiusZ, before.orbitRadiusZ, after.orbitRadiusZ);
                changed |= ShiftFloat(ref d.tubeRadius, before.tubeRadius, after.tubeRadius);
                changed |= ShiftFloat(ref d.orbitSpeedHz, before.orbitSpeedHz, after.orbitSpeedHz);
                changed |= ShiftFloat(ref d.animSpeedMultiplier, before.animSpeedMultiplier, after.animSpeedMultiplier);
                changed |= ShiftFloat(ref d.animSyncPhaseShift, before.animSyncPhaseShift, after.animSyncPhaseShift);
                if (before.vibeSlowParentEnabled != after.vibeSlowParentEnabled)
                {
                    d.vibeSlowParentEnabled = after.vibeSlowParentEnabled;
                    changed = true;
                }
                if (before.vibeInsertionAlignEnabled != after.vibeInsertionAlignEnabled)
                {
                    d.vibeInsertionAlignEnabled = after.vibeInsertionAlignEnabled;
                    changed = true;
                }
                changed |= ShiftFloat(ref d.vibeSlowParentWeight, before.vibeSlowParentWeight, after.vibeSlowParentWeight);
                changed |= ShiftFloat(ref d.vibeSlowRiseSeconds, before.vibeSlowRiseSeconds, after.vibeSlowRiseSeconds);
                changed |= ShiftFloat(ref d.vibeSlowFallSeconds, before.vibeSlowFallSeconds, after.vibeSlowFallSeconds);
                if (changed) jsonList[i] = UnityEngine.JsonUtility.ToJson(d, false);
                return changed;
            }
            return false;
        }

        private static bool ShiftFloat(ref float target, float before, float after)
        {
            if (Mathf.Approximately(before, after)) return false;
            target += after - before;
            return true;
        }

        /// <summary>
        /// アクティブプリセット中に visible が変化したら L1/L2 両スロットに反映保存する。
        /// これにより再アクティブ化や自動切替後も可視状態が維持される。
        /// </summary>
        private void SyncVisibleToActivePreset(string objId, bool visible)
        {
            if (_activePreset == null || string.IsNullOrEmpty(objId)) return;
            bool m1 = SetVisibleInJsonList(_activePreset.loop1Json, objId, visible);
            bool m2 = SetVisibleInJsonList(_activePreset.loop2Json, objId, visible);
            if (m1 || m2)
            {
                SavePresets();
                RebuildActivePresetCache();
            }
        }

        private static bool SetVisibleInJsonList(List<string> jsonList, string objId, bool visible)
        {
            if (jsonList == null) return false;
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (string.IsNullOrEmpty(jsonList[i])) continue;
                ManagedObjectData d;
                try { d = UnityEngine.JsonUtility.FromJson<ManagedObjectData>(jsonList[i]); }
                catch { continue; }
                if (d == null || !string.Equals(d.id, objId, StringComparison.Ordinal)) continue;
                if (d.visible == visible) return false;
                d.visible = visible;
                jsonList[i] = UnityEngine.JsonUtility.ToJson(d, false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// アクティブプリセット中に位相を編集したら L1/L2 両スロットへ反映する。
        /// TickActivePreset が毎フレーム Data を上書きするため、現在値だけ変えても次フレームで戻る。
        /// </summary>
        private void SyncPhaseToActivePreset(string objId, ManagedObjectData source)
        {
            if (_activePreset == null || string.IsNullOrEmpty(objId) || source == null) return;

            bool m1 = SetPhaseInJsonList(_activePreset.loop1Json, objId, source);
            bool m2 = SetPhaseInJsonList(_activePreset.loop2Json, objId, source);
            if (m1 || m2)
            {
                SavePresets();
                RebuildActivePresetCache();
            }
        }

        private static bool SetPhaseInJsonList(List<string> jsonList, string objId, ManagedObjectData source)
        {
            if (jsonList == null) return false;
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (string.IsNullOrEmpty(jsonList[i])) continue;
                ManagedObjectData d;
                try { d = UnityEngine.JsonUtility.FromJson<ManagedObjectData>(jsonList[i]); }
                catch { continue; }
                if (d == null || !string.Equals(d.id, objId, StringComparison.Ordinal)) continue;

                bool changed = false;
                if (!Mathf.Approximately(d.orbitPhaseTurns, source.orbitPhaseTurns))
                {
                    d.orbitPhaseTurns = source.orbitPhaseTurns;
                    changed = true;
                }
                if (!Mathf.Approximately(d.animSyncPhaseShift, source.animSyncPhaseShift))
                {
                    d.animSyncPhaseShift = source.animSyncPhaseShift;
                    changed = true;
                }
                if (!Mathf.Approximately(d.pistonPhaseTurns, source.pistonPhaseTurns))
                {
                    d.pistonPhaseTurns = source.pistonPhaseTurns;
                    changed = true;
                }
                if (!Mathf.Approximately(d.anglePhaseTurns, source.anglePhaseTurns))
                {
                    d.anglePhaseTurns = source.anglePhaseTurns;
                    changed = true;
                }

                if (!changed) return false;
                jsonList[i] = UnityEngine.JsonUtility.ToJson(d, false);
                return true;
            }
            return false;
        }
    }
}
