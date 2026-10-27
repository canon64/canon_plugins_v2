using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private delegate bool ReadVibeMotion(out Vector3 direction, out float body,
            out float upper, out float slow, out float gauge);
        private delegate bool ReadVibeInsertionAxis(out Vector3 axis);

        private ReadVibeMotion _vibeMotionReader;
        private ReadVibeInsertionAxis _vibeInsertionAxisReader;
        private float _vibeReaderRetryAt;
        private float _vibeAxisRetryAt;
        private readonly Dictionary<string, float> _vibeParentWeights = new Dictionary<string, float>();
        private readonly HashSet<string> _vibeAlignedIds = new HashSet<string>();

        private bool TryReadVibeInsertionAxis(out Vector3 axis)
        {
            axis = Vector3.zero;
            if (_vibeInsertionAxisReader == null && Time.unscaledTime >= _vibeAxisRetryAt)
            {
                _vibeAxisRetryAt = Time.unscaledTime + 5f;
                Type type = Type.GetType("MainGameVrDildo.Plugin, MainGameVrDildo", false);
                MethodInfo method = type?.GetMethod("TryGetInsertionAxis", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    try { _vibeInsertionAxisReader = (ReadVibeInsertionAxis)Delegate.CreateDelegate(
                        typeof(ReadVibeInsertionAxis), method); }
                    catch (Exception ex) { LogWarn("Vibe insertion-axis binding failed: " + ex.Message); }
                }
            }
            if (_vibeInsertionAxisReader == null) return false;
            try { return _vibeInsertionAxisReader(out axis) && axis.sqrMagnitude > 0.5f; }
            catch (Exception ex)
            {
                LogWarn("Vibe insertion-axis read failed: " + ex.Message);
                _vibeInsertionAxisReader = null;
                return false;
            }
        }

        private void AlignVibeRotationObjects()
        {
            if (_runtimeObjects == null) return;
            bool hasAxis = TryReadVibeInsertionAxis(out Vector3 axis);
            Quaternion axisRotation = Quaternion.identity;
            if (hasAxis)
            {
                axis.Normalize();
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis);
                if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.right, axis);
                axisRotation = Quaternion.LookRotation(axis, up.normalized);
            }
            foreach (KeyValuePair<string, RuntimeObjectRef> kv in _runtimeObjects)
            {
                RuntimeObjectRef rr = kv.Value;
                if (rr == null || rr.Data == null || rr.GameObject == null) continue;
                ManagedObjectData data = rr.Data;
                if (hasAxis && data.isRotationObject && data.vibeInsertionAlignEnabled)
                {
                    Quaternion authoredRotation = Quaternion.Euler(data.localEulerAngles);
                    Quaternion alignedRotation = axisRotation * authoredRotation;
                    // The normal piston stays aligned to the insertion axis.  During slow
                    // movement, progressively admit the parent ring's world rotation.
                    // Assigning alignedRotation unconditionally discarded the slow-parent
                    // rotation computed earlier in TickRotationOrbits.
                    float parentWeight = 0f;
                    if (data.vibeSlowParentEnabled)
                        _vibeParentWeights.TryGetValue(data.id, out parentWeight);
                    Transform parent = rr.GameObject.transform.parent;
                    Quaternion parentRotation = parent != null
                        ? parent.rotation * authoredRotation : alignedRotation;
                    rr.GameObject.transform.rotation = Quaternion.Slerp(
                        alignedRotation, parentRotation, Mathf.Clamp01(parentWeight));
                    _vibeAlignedIds.Add(data.id);
                }
                else if (_vibeAlignedIds.Remove(data.id))
                {
                    // Parent orbit already restored its own tangent orientation this frame.
                    // For unparented objects, restore the authored local orientation here.
                    ManagedObjectData parent = FindDataById(data.parentId);
                    if (parent == null || !parent.isRotationObject || !data.orientToTangent)
                        rr.GameObject.transform.localRotation = Quaternion.Euler(data.localEulerAngles);
                }
            }
        }

        private float ReadVibeSlowState()
        {
            if (_vibeMotionReader == null && Time.unscaledTime >= _vibeReaderRetryAt)
            {
                _vibeReaderRetryAt = Time.unscaledTime + 5f;
                Type type = Type.GetType("MainGameVrDildo.Plugin, MainGameVrDildo", false);
                MethodInfo method = type?.GetMethod("TryGetMotionState", BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    try { _vibeMotionReader = (ReadVibeMotion)Delegate.CreateDelegate(typeof(ReadVibeMotion), method); }
                    catch (Exception ex) { LogWarn("Vibe slow-state binding failed: " + ex.Message); }
                }
            }
            if (_vibeMotionReader == null) return 0f;
            try
            {
                Vector3 direction;
                float body, upper, slow, gauge;
                return _vibeMotionReader(out direction, out body, out upper, out slow, out gauge)
                    ? Mathf.Clamp01(slow) : 0f;
            }
            catch (Exception ex)
            {
                LogWarn("Vibe slow-state read failed: " + ex.Message);
                _vibeMotionReader = null;
                return 0f;
            }
        }

        private float UpdateVibeParentWeight(ManagedObjectData child, float slow)
        {
            if (child == null || string.IsNullOrEmpty(child.id)) return 0f;
            _vibeParentWeights.TryGetValue(child.id, out float current);
            float maximum = Mathf.Clamp01(child.vibeSlowParentWeight);
            float target = child.vibeSlowParentEnabled ? slow * maximum : 0f;
            float duration = target > current ? child.vibeSlowRiseSeconds : child.vibeSlowFallSeconds;
            if (MotionTraceEnabled) TraceParentTarget(child, target, current, duration);
            current = Mathf.MoveTowards(current, target,
                Time.unscaledDeltaTime * Mathf.Max(maximum, current) / Mathf.Max(0.05f, duration));
            if (current < 0.0001f) _vibeParentWeights.Remove(child.id);
            else _vibeParentWeights[child.id] = current;
            return current;
        }
    }
}
