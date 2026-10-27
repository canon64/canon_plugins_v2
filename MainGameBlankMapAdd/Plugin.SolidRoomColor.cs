using System.Collections.Generic;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private Material _solidRoomColorMaterial;
        private readonly Dictionary<Renderer, Material> _solidRoomOriginalMaterials =
            new Dictionary<Renderer, Material>();
        private bool _solidRoomColorApplied;
        private Color _lastSolidRoomColor = new Color(-1f, -1f, -1f, 1f);

        private void UpdateSolidRoomColorMode()
        {
            bool enabled = _settings != null && _settings.SolidRoomColorEnabled && _videoRoomRoot != null;
            if (!enabled)
            {
                RestoreSolidRoomMaterials();
                return;
            }

            Color color = ResolveSolidRoomColor();
            EnsureSolidRoomColorMaterial();
            if (_solidRoomColorMaterial == null)
                return;

            if (!_solidRoomColorApplied || !ApproximatelyColor(_lastSolidRoomColor, color))
                ApplySolidRoomColorToMaterial(color);

            bool any = false;
            foreach (Renderer renderer in EnumerateVideoSurfaceRenderers())
            {
                if (renderer == null)
                    continue;

                any = true;
                if (!_solidRoomOriginalMaterials.ContainsKey(renderer))
                    _solidRoomOriginalMaterials[renderer] = renderer.sharedMaterial;

                if (renderer.sharedMaterial != _solidRoomColorMaterial)
                    renderer.sharedMaterial = _solidRoomColorMaterial;
            }

            PruneSolidRoomMaterialCache();
            _solidRoomColorApplied = any;
        }

        private IEnumerable<Renderer> EnumerateVideoSurfaceRenderers()
        {
            if (_videoRoomRoot == null)
                yield break;

            Transform root = _videoRoomRoot.transform;
            int count = root.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.gameObject == null)
                    continue;
                if (!child.name.StartsWith("VideoSurface_", System.StringComparison.Ordinal))
                    continue;

                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer != null)
                    yield return renderer;
            }
        }

        private Color ResolveSolidRoomColor()
        {
            if (_settings == null)
                return Color.black;

            return new Color(
                Mathf.Clamp01(_settings.SolidRoomColorR),
                Mathf.Clamp01(_settings.SolidRoomColorG),
                Mathf.Clamp01(_settings.SolidRoomColorB),
                1f);
        }

        private void EnsureSolidRoomColorMaterial()
        {
            if (_solidRoomColorMaterial != null)
                return;

            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
            if (shader == null)
                return;

            _solidRoomColorMaterial = new Material(shader)
            {
                name = "SolidRoomColorMaterial"
            };
            if (_solidRoomColorMaterial.HasProperty("_Cull"))
                _solidRoomColorMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            if (_solidRoomColorMaterial.HasProperty("_ZWrite"))
                _solidRoomColorMaterial.SetInt("_ZWrite", 1);
            if (_solidRoomColorMaterial.HasProperty("_Glossiness"))
                _solidRoomColorMaterial.SetFloat("_Glossiness", 0f);
            if (_solidRoomColorMaterial.HasProperty("_Metallic"))
                _solidRoomColorMaterial.SetFloat("_Metallic", 0f);
            _solidRoomColorMaterial.EnableKeyword("_EMISSION");
            ApplySolidRoomColorToMaterial(ResolveSolidRoomColor());
        }

        private void ApplySolidRoomColorToMaterial(Color color)
        {
            if (_solidRoomColorMaterial == null)
                return;

            if (_solidRoomColorMaterial.HasProperty("_Color"))
                _solidRoomColorMaterial.SetColor("_Color", color);
            if (_solidRoomColorMaterial.HasProperty("_TintColor"))
                _solidRoomColorMaterial.SetColor("_TintColor", color);
            if (_solidRoomColorMaterial.HasProperty("_EmissionColor"))
                _solidRoomColorMaterial.SetColor("_EmissionColor", color);

            _lastSolidRoomColor = color;
        }

        private void RestoreSolidRoomMaterials()
        {
            if (!_solidRoomColorApplied && _solidRoomOriginalMaterials.Count == 0)
                return;

            foreach (KeyValuePair<Renderer, Material> entry in _solidRoomOriginalMaterials)
            {
                Renderer renderer = entry.Key;
                if (renderer != null)
                    renderer.sharedMaterial = entry.Value;
            }

            _solidRoomOriginalMaterials.Clear();
            _solidRoomColorApplied = false;
        }

        private void PruneSolidRoomMaterialCache()
        {
            if (_solidRoomOriginalMaterials.Count == 0)
                return;

            List<Renderer> dead = null;
            foreach (KeyValuePair<Renderer, Material> entry in _solidRoomOriginalMaterials)
            {
                if (entry.Key != null)
                    continue;
                if (dead == null)
                    dead = new List<Renderer>();
                dead.Add(entry.Key);
            }

            if (dead == null)
                return;

            for (int i = 0; i < dead.Count; i++)
                _solidRoomOriginalMaterials.Remove(dead[i]);
        }

        private static bool ApproximatelyColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= 0.0001f
                && Mathf.Abs(a.g - b.g) <= 0.0001f
                && Mathf.Abs(a.b - b.b) <= 0.0001f
                && Mathf.Abs(a.a - b.a) <= 0.0001f;
        }

        private void DestroySolidRoomColorResources()
        {
            RestoreSolidRoomMaterials();
            if (_solidRoomColorMaterial != null)
            {
                Destroy(_solidRoomColorMaterial);
                _solidRoomColorMaterial = null;
            }
            _lastSolidRoomColor = new Color(-1f, -1f, -1f, 1f);
        }
    }
}
