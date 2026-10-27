using System;
using UnityEngine;

namespace MainGameBlankMapAdd.Media.Images
{
    // All tiles in one image layer have identical local quad geometry and UVs.
    // Keep one mesh and reusable buffers per layer instead of touching every tile.
    internal sealed class SlideshowUvMesh : IDisposable
    {
        internal Mesh Mesh { get; }
        private readonly Vector2[] _baseUv, _uv;
        private Vector2 _scale, _offset;
        private bool _applied;

        internal SlideshowUvMesh(Mesh template, string name, bool preserveTemplateUvs = false)
        {
            Mesh = UnityEngine.Object.Instantiate(template);
            Mesh.name = name;
            Mesh.MarkDynamic();
            Vector3[] vertices = Mesh.vertices;
            Vector2[] templateUvs = preserveTemplateUvs ? Mesh.uv : null;
            Bounds bounds = Mesh.bounds;
            _baseUv = new Vector2[vertices.Length];
            _uv = new Vector2[vertices.Length];
            float width = Mathf.Abs(bounds.size.x), height = Mathf.Abs(bounds.size.y);
            for (int i = 0; i < vertices.Length; i++)
                _baseUv[i] = templateUvs != null && templateUvs.Length == vertices.Length
                    ? templateUvs[i]
                    : new Vector2(width > 0.0001f ? (vertices[i].x - bounds.min.x) / width : 0.5f,
                        height > 0.0001f ? (vertices[i].y - bounds.min.y) / height : 0.5f);
        }

        internal void Apply(Vector2 scale, Vector2 offset)
        {
            if (_applied && _scale.Equals(scale) && _offset.Equals(offset)) return;
            for (int i = 0; i < _uv.Length; i++)
                _uv[i] = new Vector2(offset.x + scale.x * _baseUv[i].x, offset.y + scale.y * _baseUv[i].y);
            Mesh.uv = _uv;
            _scale = scale;
            _offset = offset;
            _applied = true;
        }

        public void Dispose() { if (Mesh != null) UnityEngine.Object.Destroy(Mesh); }

        internal static void DestroyTile(GameObject quad)
        {
            if (quad == null) return;
            // Destroy is deferred. Remove the child immediately so its disappearance
            // next frame does not trigger another layout rebuild. The layer owns Mesh.
            quad.SetActive(false);
            quad.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(quad);
        }
    }
}
