using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace MainGameVrNeckChain
{
    public sealed partial class Plugin
    {
        private sealed class BodyCollisionEntry
        {
            internal string Label;
            internal Transform Start;
            internal Transform End;
            internal DynamicBoneCollider Source;
            internal DynamicBoneCollider Collider;
            internal GameObject Host;
            internal GameObject Preview;
            internal float BaseRadius;
            internal ConfigEntry<float> OffsetX, OffsetY, OffsetZ;
            internal ConfigEntry<float> RotationX, RotationY, RotationZ;
            internal ConfigEntry<float> RadiusScale, LengthScale;
        }

        private readonly List<BodyCollisionEntry> _bodyCollisions = new List<BodyCollisionEntry>();
        private Material _bodyPreviewMaterial;
        private Material _breastPreviewMaterial;
        private int _selectedBodyCollision;

        private void SetupBodyColliders()
        {
            ClearBodyColliders();
            if (_dynamicBone.m_Colliders == null)
                _dynamicBone.m_Colliders = new List<DynamicBoneCollider>();
            AddBodyCapsule("cf_j_neck", "cf_j_spine03", 0.085f);
            AddBodyCapsule("cf_j_spine03", "cf_j_spine02", 0.15f);
            AddBodyCapsule("cf_j_spine02", "cf_j_spine01", 0.16f);
            AddBodyCapsule("cf_j_spine01", "cf_j_waist01", 0.15f);
            AddBodyCapsule("cf_j_waist01", "cf_j_hips", 0.14f);

            bool nativeLeft = false, nativeRight = false;
            int ordinal = 0;
            foreach (DynamicBoneCollider source in _female.GetComponentsInChildren<DynamicBoneCollider>(true))
            {
                if (source == null || !source.enabled ||
                    source.m_Bound != DynamicBoneCollider.Bound.Outside) continue;
                string boneName = BreastBoneName(source.transform);
                if (boneName.Length == 0) continue;
                AddNativeBreastProxy(source, boneName, ordinal++);
                if (boneName.EndsWith("_L", StringComparison.OrdinalIgnoreCase)) nativeLeft = true;
                if (boneName.EndsWith("_R", StringComparison.OrdinalIgnoreCase)) nativeRight = true;
            }
            if (!nativeLeft)
            {
                AddBodyCapsule("cf_j_bust01_L", "cf_j_bust03_L", 0.09f);
                AddBodyCapsule("cf_j_bust03_L", "cf_j_bnip02_L", 0.07f);
            }
            if (!nativeRight)
            {
                AddBodyCapsule("cf_j_bust01_R", "cf_j_bust03_R", 0.09f);
                AddBodyCapsule("cf_j_bust03_R", "cf_j_bnip02_R", 0.07f);
            }
            UpdateBodyColliders();
            Logger.LogInfo("Chain collision shapes=" + _bodyCollisions.Count +
                " native breast copies=" + ordinal);
        }

        private static string BreastBoneName(Transform node)
        {
            while (node != null)
            {
                string name = node.name;
                if (name.IndexOf("bust", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("bnip", StringComparison.OrdinalIgnoreCase) >= 0)
                    return name;
                node = node.parent;
            }
            return string.Empty;
        }

        private ConfigEntry<float> BindCollisionSetting(string section, string name, float value,
            float minimum, float maximum)
            => Config.Bind(section, name, value,
                new ConfigDescription("鎖専用コライダーの" + name,
                    new AcceptableValueRange<float>(minimum, maximum)));

        private BodyCollisionEntry CreateCollisionEntry(string key, string label, float radius)
        {
            string section = "Chain collider " + key;
            var host = new GameObject("NeckChainCollider_" + key);
            var collider = host.AddComponent<DynamicBoneCollider>();
            collider.m_Bound = DynamicBoneCollider.Bound.Outside;
            collider.m_Direction = DynamicBoneCollider.Direction.Y;
            collider.m_Radius = radius;
            var entry = new BodyCollisionEntry
            {
                Label = label, Host = host, Collider = collider, BaseRadius = radius,
                OffsetX = BindCollisionSetting(section, "Position X (m)", 0f, -0.3f, 0.3f),
                OffsetY = BindCollisionSetting(section, "Position Y (m)", 0f, -0.3f, 0.3f),
                OffsetZ = BindCollisionSetting(section, "Position Z (m)", 0f, -0.3f, 0.3f),
                RotationX = BindCollisionSetting(section, "Rotation X (deg)", 0f, -180f, 180f),
                RotationY = BindCollisionSetting(section, "Rotation Y (deg)", 0f, -180f, 180f),
                RotationZ = BindCollisionSetting(section, "Rotation Z (deg)", 0f, -180f, 180f),
                RadiusScale = BindCollisionSetting(section, "Radius scale", 1f, 0.2f, 3f),
                LengthScale = BindCollisionSetting(section, "Length scale", 1f, 0.2f, 3f)
            };
            entry.Preview = CreateColliderPreview("NeckChainColliderPreview_" + key);
            _bodyCollisions.Add(entry);
            _dynamicBone.m_Colliders.Add(collider);
            return entry;
        }

        private void AddBodyCapsule(string startName, string endName, float radius)
        {
            Transform start = FindBone(_female, startName);
            Transform end = FindBone(_female, endName);
            if (start == null || end == null) return;
            BodyCollisionEntry entry = CreateCollisionEntry(startName + "_to_" + endName,
                startName + " → " + endName, radius);
            entry.Start = start;
            entry.End = end;
        }

        private void AddNativeBreastProxy(DynamicBoneCollider source, string boneName, int ordinal)
        {
            BodyCollisionEntry entry = CreateCollisionEntry("breast_" + boneName + "_" + ordinal,
                "胸（既存） " + boneName, source.m_Radius);
            entry.Source = source;
            entry.Collider.m_Direction = source.m_Direction;
        }

        private void UpdateBodyColliders()
        {
            foreach (BodyCollisionEntry entry in _bodyCollisions)
            {
                if (entry.Host == null) continue;
                Vector3 offset = new Vector3(entry.OffsetX.Value, entry.OffsetY.Value, entry.OffsetZ.Value);
                Quaternion adjustment = Quaternion.Euler(entry.RotationX.Value,
                    entry.RotationY.Value, entry.RotationZ.Value);
                if (entry.Source != null)
                {
                    Transform source = entry.Source.transform;
                    entry.Host.transform.SetPositionAndRotation(
                        source.TransformPoint(entry.Source.m_Center + offset), source.rotation * adjustment);
                    entry.Host.transform.localScale = source.lossyScale;
                    entry.Collider.m_Height = entry.Source.m_Height * entry.LengthScale.Value;
                    entry.Collider.m_Direction = entry.Source.m_Direction;
                    entry.Collider.enabled = entry.Source.enabled;
                }
                else if (entry.Start != null && entry.End != null)
                {
                    Vector3 axis = entry.End.position - entry.Start.position;
                    Quaternion baseRotation = axis.sqrMagnitude > 0.000001f
                        ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                    entry.Host.transform.SetPositionAndRotation(
                        (entry.Start.position + entry.End.position) * 0.5f + baseRotation * offset,
                        baseRotation * adjustment);
                    entry.Host.transform.localScale = Vector3.one;
                    entry.Collider.m_Height = (axis.magnitude + entry.BaseRadius) * entry.LengthScale.Value;
                }
                entry.Collider.m_Radius = entry.BaseRadius * entry.RadiusScale.Value;
            }
        }

        private void ResolveBodyCollisions(bool rootPinned, bool tipPinned)
        {
            for (int i = rootPinned ? 1 : 0; i < BoneCount - (tipPinned ? 1 : 0); i++)
                _points[i] = PushOutsideBody(_points[i]);
        }

        private Vector3 PushOutsideBody(Vector3 point)
        {
            foreach (BodyCollisionEntry entry in _bodyCollisions)
                if (entry.Collider != null && entry.Collider.enabled)
                    entry.Collider.Collide(ref point, 0f);
            return point;
        }

        private GameObject CreateColliderPreview(string name)
        {
            var preview = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            preview.name = name;
            Collider physics = preview.GetComponent<Collider>();
            if (physics != null) DestroyImmediate(physics);
            Renderer renderer = preview.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            preview.SetActive(false);
            return preview;
        }

        private Material ColliderPreviewMaterial(bool native)
        {
            Material material = native ? _breastPreviewMaterial : _bodyPreviewMaterial;
            if (material != null) return material;
            Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 3000;
            material.color = native ? new Color(1f, 0.35f, 0.9f, 0.25f)
                : new Color(0.2f, 0.9f, 1f, 0.25f);
            if (native) _breastPreviewMaterial = material;
            else _bodyPreviewMaterial = material;
            return material;
        }

        private void UpdateColliderPreviews()
        {
            bool show = _showColliderPreviews != null && _showColliderPreviews.Value;
            foreach (BodyCollisionEntry entry in _bodyCollisions)
            {
                if (entry.Preview == null || entry.Collider == null) continue;
                bool visible = show && entry.Collider.enabled;
                if (entry.Preview.activeSelf != visible) entry.Preview.SetActive(visible);
                if (!visible) continue;
                Renderer renderer = entry.Preview.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = ColliderPreviewMaterial(entry.Source != null);
                DynamicBoneCollider collider = entry.Collider;
                Transform host = entry.Host.transform;
                float half = Mathf.Max(0f, (collider.m_Height - collider.m_Radius) * 0.5f);
                Vector3 direction = collider.m_Direction == DynamicBoneCollider.Direction.X ? Vector3.right :
                    collider.m_Direction == DynamicBoneCollider.Direction.Z ? Vector3.forward : Vector3.up;
                Vector3 a = host.TransformPoint(-direction * half);
                Vector3 b = host.TransformPoint(direction * half);
                float radius = collider.m_Radius * Mathf.Abs(host.lossyScale.z);
                Vector3 axis = b - a;
                entry.Preview.transform.SetPositionAndRotation((a + b) * 0.5f,
                    axis.sqrMagnitude > 0.000001f
                        ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : host.rotation);
                entry.Preview.transform.localScale = new Vector3(
                    radius * 2f, (axis.magnitude + radius * 2f) * 0.5f, radius * 2f);
            }
        }

        private void ClearBodyColliders()
        {
            foreach (BodyCollisionEntry entry in _bodyCollisions)
            {
                if (_dynamicBone != null && _dynamicBone.m_Colliders != null && entry.Collider != null)
                    _dynamicBone.m_Colliders.Remove(entry.Collider);
                if (entry.Preview != null) Destroy(entry.Preview);
                if (entry.Host != null) Destroy(entry.Host);
            }
            _bodyCollisions.Clear();
            _selectedBodyCollision = 0;
            if (_bodyPreviewMaterial != null) Destroy(_bodyPreviewMaterial);
            if (_breastPreviewMaterial != null) Destroy(_breastPreviewMaterial);
            _bodyPreviewMaterial = null;
            _breastPreviewMaterial = null;
        }
    }
}
