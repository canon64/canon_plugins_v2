using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralMode
{
    public partial class Plugin
    {
        private const int EdgeFadeGrid = 64;
        private const float EdgeFadeStart = 0.72f;
        private const float EdgeFadeEnd = 0.98f;

        private readonly Dictionary<int, Mesh> _edgeFadeOriginalMeshes = new Dictionary<int, Mesh>();
        private readonly Dictionary<int, Material> _edgeFadeOriginalMaterials = new Dictionary<int, Material>();
        private Mesh _edgeFadeMesh;
        private Material _edgeFadeMaterial;
        private float _nextEdgeFadeScanTime;

        private void UpdateEdgeFadeDisplay()
        {
            if (_state != AstralState.Astral
                && _state != AstralState.FadeIn
                && _state != AstralState.ReturnWarping)
            {
                RestoreEdgeFadeDisplay();
                return;
            }

            if (Time.unscaledTime < _nextEdgeFadeScanTime)
                return;

            _nextEdgeFadeScanTime = Time.unscaledTime + 1.0f;

            if (_lastSkyFade != _skyFade.Value || _lastSkyCenterOpacity != _skyCenterOpacity.Value) {
                _lastSkyFade=_skyFade.Value;
                _lastSkyCenterOpacity=_skyCenterOpacity.Value;
                if(_edgeFadeMesh!=null) UnityEngine.Object.Destroy(_edgeFadeMesh);
                _edgeFadeMesh=null;
            }
            Mesh fadeMesh = GetOrCreateEdgeFadeMesh();
            Material fadeMaterial = GetOrCreateEdgeFadeMaterial();
            if (fadeMesh == null || fadeMaterial == null)
                return;

            // RenderTexture を表示する板は _displayQuads で管理している。
            // シーン全体の MeshRenderer 探索は不要。
            for (int i = 0; i < _displayQuads.Count; i++)
            {
                GameObject quad = _displayQuads[i];
                if (quad == null || !quad.activeInHierarchy)
                    continue;

                MeshRenderer mr = quad.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled)
                    continue;

                Material srcMat = mr.sharedMaterial;
                if (srcMat == null || srcMat.mainTexture == null)
                    continue;

                if (!(srcMat.mainTexture is RenderTexture))
                    continue;

                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf == null)
                    continue;

                int id = mr.GetInstanceID();

                if (!_edgeFadeOriginalMeshes.ContainsKey(id))
                    _edgeFadeOriginalMeshes[id] = mf.sharedMesh;

                if (!_edgeFadeOriginalMaterials.ContainsKey(id))
                    _edgeFadeOriginalMaterials[id] = srcMat;

                fadeMaterial.mainTexture = srcMat.mainTexture;

                mf.sharedMesh = fadeMesh;
                mr.sharedMaterial = fadeMaterial;
            }
        }

        private void RestoreEdgeFadeDisplay()
        {
            if (_edgeFadeOriginalMeshes.Count == 0 && _edgeFadeOriginalMaterials.Count == 0)
                return;

            MeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null)
                    continue;

                int id = mr.GetInstanceID();
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf != null && _edgeFadeOriginalMeshes.TryGetValue(id, out Mesh mesh))
                    mf.sharedMesh = mesh;
                if (_edgeFadeOriginalMaterials.TryGetValue(id, out Material mat))
                    mr.sharedMaterial = mat;
            }

            _edgeFadeOriginalMeshes.Clear();
            _edgeFadeOriginalMaterials.Clear();
        }

        private Mesh GetOrCreateEdgeFadeMesh()
        {
            if (_edgeFadeMesh != null)
                return _edgeFadeMesh;

            int grid = Mathf.Max(2, EdgeFadeGrid);
            int vertCount = (grid + 1) * (grid + 1);

            Vector3[] vertices = new Vector3[vertCount];
            Vector2[] uv = new Vector2[vertCount];
            Color[] colors = new Color[vertCount];

            int vi = 0;
            for (int y = 0; y <= grid; y++)
            {
                float fy = y / (float)grid;
                for (int x = 0; x <= grid; x++)
                {
                    float fx = x / (float)grid;

                    vertices[vi] = new Vector3(fx - 0.5f, fy - 0.5f, 0f);
                    uv[vi] = new Vector2(fx, fy);

                    float dx = Mathf.Abs(fx - 0.5f) * 2f;
                    float dy = Mathf.Abs(fy - 0.5f) * 2f;
                    float edge = Mathf.Sqrt(dx * dx + dy * dy);
                    float fadeProgress = Mathf.InverseLerp(1f - _skyFade.Value, 1f, edge);
                    float alpha = (1f - Mathf.SmoothStep(0f, 1f, fadeProgress)) * Mathf.Clamp01(_skyCenterOpacity.Value / 100f);

                    colors[vi] = new Color(1f, 1f, 1f, alpha);
                    vi++;
                }
            }

            List<int> tris = new List<int>(grid * grid * 6);
            for (int y = 0; y < grid; y++)
            {
                for (int x = 0; x < grid; x++)
                {
                    int a = y * (grid + 1) + x;
                    int b = a + 1;
                    int c = a + grid + 1;
                    int d = c + 1;

                    tris.Add(a);
                    tris.Add(c);
                    tris.Add(b);

                    tris.Add(b);
                    tris.Add(c);
                    tris.Add(d);
                }
            }

            _edgeFadeMesh = new Mesh();
            _edgeFadeMesh.name = "AstralMode_EdgeFadeMesh";
            _edgeFadeMesh.vertices = vertices;
            _edgeFadeMesh.uv = uv;
            _edgeFadeMesh.colors = colors;
            _edgeFadeMesh.triangles = tris.ToArray();
            _edgeFadeMesh.RecalculateBounds();
            _edgeFadeMesh.RecalculateNormals();
            _edgeFadeMesh.hideFlags = HideFlags.HideAndDontSave;

            return _edgeFadeMesh;
        }

        private Material GetOrCreateEdgeFadeMaterial()
        {
            if (_edgeFadeMaterial != null)
                return _edgeFadeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            if (shader == null)
                return null;

            _edgeFadeMaterial = new Material(shader);
            _edgeFadeMaterial.name = "AstralMode_EdgeFadeMaterial";
            _edgeFadeMaterial.hideFlags = HideFlags.HideAndDontSave;
            _edgeFadeMaterial.renderQueue = 3000;

            return _edgeFadeMaterial;
        }
    }
}
