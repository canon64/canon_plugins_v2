using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private void RecordUndoSnapshot(string reason)
        {
            SyncAllDataFromRuntime();
            string snapshotJson = SerializeCurrentLayoutSnapshot();
            _undoStack.Push(snapshotJson);
            TrimHistoryStack(_undoStack, Mathf.Max(8, _settings.MaxUndoSteps));
            _redoStack.Clear();
            LogDebug("undo push reason=" + reason + " undoCount=" + _undoStack.Count);
        }

        private void Undo()
        {
            if (_undoStack.Count <= 0)
            {
                LogWarn("undo skipped: stack empty");
                return;
            }

            SyncAllDataFromRuntime();
            string current = SerializeCurrentLayoutSnapshot();
            string prev = _undoStack.Pop();
            _redoStack.Push(current);

            RestoreSnapshotJson(prev, rebuildRuntime: true);
            SaveLayoutIfNeeded();
            LogInfo("undo applied");
        }

        private void Redo()
        {
            if (_redoStack.Count <= 0)
            {
                LogWarn("redo skipped: stack empty");
                return;
            }

            SyncAllDataFromRuntime();
            string current = SerializeCurrentLayoutSnapshot();
            string next = _redoStack.Pop();
            _undoStack.Push(current);
            TrimHistoryStack(_undoStack, Mathf.Max(8, _settings.MaxUndoSteps));

            RestoreSnapshotJson(next, rebuildRuntime: true);
            SaveLayoutIfNeeded();
            LogInfo("redo applied");
        }

        private static void TrimHistoryStack(Stack<string> stack, int maxCount)
        {
            if (stack == null || stack.Count <= maxCount)
            {
                return;
            }

            string[] values = stack.ToArray();
            stack.Clear();

            int keep = Mathf.Min(maxCount, values.Length);
            for (int i = keep - 1; i >= 0; i--)
            {
                stack.Push(values[i]);
            }
        }

        private string SerializeCurrentLayoutSnapshot()
        {
            var file = new ObjectLayoutFile
            {
                format = "ObjectLayoutV2",
                objectsJson = EncodeObjects(_objects),
                selectedId = _selectedId,
                parentCandidateKind = _parentCandidateKind,
                parentCandidateRefId = _parentCandidateRefId,
                parentCandidateId = string.Equals(_parentCandidateKind, ParentKindManaged, StringComparison.Ordinal)
                    ? _parentCandidateRefId
                    : null
            };
            return JsonUtility.ToJson(file, false);
        }

        private void RestoreSnapshotJson(string json, bool rebuildRuntime)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                LogWarn("restore snapshot skipped: empty json");
                return;
            }

            ObjectLayoutFile parsed = null;
            try
            {
                parsed = JsonUtility.FromJson<ObjectLayoutFile>(json);
            }
            catch (Exception ex)
            {
                LogError("restore snapshot parse failed: " + ex.Message);
                return;
            }

            ApplyLayoutData(parsed, rebuildRuntime);
        }

        private List<ManagedObjectData> CloneObjectList(List<ManagedObjectData> source)
        {
            var cloned = new List<ManagedObjectData>();
            if (source == null)
            {
                return cloned;
            }

            for (int i = 0; i < source.Count; i++)
            {
                ManagedObjectData item = source[i];
                if (item == null)
                {
                    continue;
                }

                cloned.Add(CloneObject(item));
            }

            return cloned;
        }

        private ManagedObjectData CloneObject(ManagedObjectData source)
        {
            if (source == null)
            {
                return null;
            }

            return new ManagedObjectData
            {
                id = source.id,
                name = source.name,
                primitive = source.primitive,
                parentKind = source.parentKind,
                parentRefId = source.parentRefId,
                parentId = source.parentId,
                localPosition = source.localPosition,
                localEulerAngles = source.localEulerAngles,
                localScale = source.localScale,
                autoRotate = source.autoRotate,
                autoRotateAxis = source.autoRotateAxis,
                autoRotateSpeedDegPerSec = source.autoRotateSpeedDegPerSec,
                autoRotateLocalSpace = source.autoRotateLocalSpace,

                // motionMode + Angle/Piston
                motionMode = source.motionMode,
                angleAxis = source.angleAxis,
                angleAmplitudeDeg = source.angleAmplitudeDeg,
                angleSpeedHz = source.angleSpeedHz,
                anglePhaseTurns = source.anglePhaseTurns,
                angleLocalSpace = source.angleLocalSpace,
                pistonAxis = source.pistonAxis,
                pistonAmplitude = source.pistonAmplitude,
                pistonSpeedHz = source.pistonSpeedHz,
                pistonPhaseTurns = source.pistonPhaseTurns,
                pistonLocalSpace = source.pistonLocalSpace,

                // 回転オブジェクト本体パラメータ
                isRotationObject = source.isRotationObject,
                orbitRadiusX = source.orbitRadiusX,
                orbitRadiusZ = source.orbitRadiusZ,
                tubeRadius = source.tubeRadius,
                orbitSpeedHz = source.orbitSpeedHz,
                vibeSlowParentEnabled = source.vibeSlowParentEnabled,
                vibeInsertionAlignEnabled = source.vibeInsertionAlignEnabled,
                vibeSlowParentWeight = source.vibeSlowParentWeight,
                vibeSlowRiseSeconds = source.vibeSlowRiseSeconds,
                vibeSlowFallSeconds = source.vibeSlowFallSeconds,
                animSync = source.animSync,
                animSpeedMultiplier = source.animSpeedMultiplier,
                phaseContinuityOffsetTurns = source.phaseContinuityOffsetTurns,
                animSyncPhaseShift = source.animSyncPhaseShift,
                orientChildrenToTangent = source.orientChildrenToTangent,

                // 回転オブジェクトの子側パラメータ
                orbitPhaseTurns = source.orbitPhaseTurns,
                orientToTangent = source.orientToTangent,

                // ピストン/アングル ドライバ本体
                isPistonObject = source.isPistonObject,
                pistonRodRadius = source.pistonRodRadius,
                isAngleObject = source.isAngleObject,
                angleFanRadius = source.angleFanRadius,

                // 表示
                visible = source.visible
            };
        }

        private void ApplyLayoutData(ObjectLayoutFile file, bool rebuildRuntime)
        {
            _objects.Clear();

            if (file != null && file.objectsJson != null)
            {
                List<ManagedObjectData> decoded = DecodeObjects(file.objectsJson);
                for (int i = 0; i < decoded.Count; i++)
                {
                    ManagedObjectData item = decoded[i];
                    if (item == null) continue;
                    NormalizeData(item);
                    _objects.Add(item);
                }
            }

            bool addedMissingRotationHooks = EnsureRotationObjectHooks();

            _selectedId = file != null ? file.selectedId : null;
            if (FindDataById(_selectedId) == null)
            {
                _selectedId = _objects.Count > 0 ? _objects[0].id : null;
            }

            string candidateKind = file != null ? file.parentCandidateKind : null;
            string candidateRefId = file != null ? file.parentCandidateRefId : null;
            if (string.IsNullOrEmpty(candidateKind) && file != null && !string.IsNullOrEmpty(file.parentCandidateId))
            {
                candidateKind = ParentKindManaged;
                candidateRefId = file.parentCandidateId;
            }

            if (string.IsNullOrEmpty(candidateKind) || string.Equals(candidateKind, ParentKindRoot, StringComparison.Ordinal))
            {
                _parentCandidateKind = ParentKindRoot;
                _parentCandidateRefId = null;
            }
            else if (string.Equals(candidateKind, ParentKindManaged, StringComparison.Ordinal))
            {
                _parentCandidateKind = ParentKindManaged;
                _parentCandidateRefId = FindDataById(candidateRefId) != null ? candidateRefId : null;
                if (string.IsNullOrEmpty(_parentCandidateRefId))
                {
                    _parentCandidateKind = ParentKindRoot;
                }
            }
            else if (string.Equals(candidateKind, ParentKindExternal, StringComparison.Ordinal))
            {
                _parentCandidateKind = ParentKindExternal;
                _parentCandidateRefId = candidateRefId;
            }
            else
            {
                _parentCandidateKind = ParentKindRoot;
                _parentCandidateRefId = null;
            }

            _selectionDirty = true;
            _selectedGizmoUndoCaptured = false;
            if (rebuildRuntime)
            {
                RebuildAllRuntimeObjects();
            }
            if (addedMissingRotationHooks)
            {
                SaveLayoutIfNeeded();
            }
            RaiseManagedObjectListChanged();
        }
    }
}
