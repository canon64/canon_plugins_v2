// Minimal Unity API adapters: exercise the production UV math and deferred-destroy
// ownership contract without starting the game. These are not rendering tests.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal class Object
    {
        internal bool DestroyQueued;
        public static Mesh Instantiate(Mesh mesh)
        {
            var clone = new Mesh(mesh.vertices, mesh.bounds);
            Vector2[] uv = mesh.uv;
            if (uv != null) clone.uv = uv;
            return clone;
        }
        public static void Destroy(Object obj) { obj.DestroyQueued = true; }
    }
    internal struct Vector2 : IEquatable<Vector2>
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public bool Equals(Vector2 other) => x == other.x && y == other.y;
    }
    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    internal struct Bounds { public Vector3 min, size; }
    internal static class Mathf { public static float Abs(float f) => Math.Abs(f); }
    internal class Mesh : Object
    {
        public string name;
        public Bounds bounds;
        private readonly Vector3[] _vertices;
        private Vector2[] _uv;
        public int VertexReads, UvReads, UvWrites;
        public Vector2[] LastUvArgument;
        public Mesh(Vector3[] vertices, Bounds bounds) { _vertices = vertices; this.bounds = bounds; }
        public Vector3[] vertices { get { VertexReads++; return (Vector3[])_vertices.Clone(); } }
        public Vector2[] uv
        {
            get { UvReads++; return _uv == null ? null : (Vector2[])_uv.Clone(); }
            set { UvWrites++; LastUvArgument = value; _uv = (Vector2[])value.Clone(); }
        }
        public void MarkDynamic() { }
    }
    internal class Transform
    {
        private Transform _parent;
        private readonly List<Transform> _children = new List<Transform>();
        public int childCount => _children.Count;
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            _parent?._children.Remove(this);
            _parent = parent;
            _parent?._children.Add(this);
        }
    }
    internal class GameObject : Object
    {
        public readonly Transform transform = new Transform();
        public bool activeSelf = true;
        public void SetActive(bool active) { activeSelf = active; }
    }
}
