using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace MainGameMilkSpray
{
    internal sealed class LiquidRenderer : IDisposable
    {
        // 20 sphere vertices + 4 glow vertices = 24 per visible particle.
        private const int Longitudes = 6, Latitudes = 4;
        private const int Capacity = LiquidSimulation.MaxDrops + LiquidSimulation.MaxGhosts;
        private readonly DrawParticle[] _particles = new DrawParticle[Capacity];
        private readonly Vector3[] _normals;
        private readonly int[] _triangles;
        private readonly MeshBuffer _buffer;
        private AssetBundle _bundle;
        private GameObject _root;
        private Mesh _mesh;
        private Material _material;
        private MeshRenderer _renderer;
        private int _count;
        private bool _disposed;
        private readonly MilkPerformanceLog _performance;
        private readonly Dictionary<int, string> _cameraNames = new Dictionary<int, string>();

        private struct DrawParticle
        {
            public Vector3 Center, Axis;
            public float Diameter, Length, Depth;
            public Color Color;
            public Vector4 Surface;
            public int Order;
        }
        private sealed class MeshBuffer
        {
            private readonly List<Vector3> _positions, _normals;
            private readonly List<Color> _colors;
            private readonly List<Vector4> _surfaces;
            private readonly List<Vector2> _glowUvs;
            private readonly List<int> _indices;
            internal int VertexCount => _positions.Count;
            internal int TriangleCount => _indices.Count / 3;

            public MeshBuffer(int vertices, int indices)
            {
                int vertexCapacity = Capacity * (vertices + 4);
                _positions = new List<Vector3>(vertexCapacity);
                _normals = new List<Vector3>(vertexCapacity);
                _colors = new List<Color>(vertexCapacity);
                _surfaces = new List<Vector4>(vertexCapacity);
                _glowUvs = new List<Vector2>(vertexCapacity);
                _indices = new List<int>(Capacity * (indices + 6));
            }
            public void Clear()
            {
                _positions.Clear(); _normals.Clear(); _colors.Clear();
                _surfaces.Clear(); _glowUvs.Clear(); _indices.Clear();
            }
            public void Add(DrawParticle particle, Vector3[] normals, int[] triangles, Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward)
            {
                float radius = particle.Diameter * .5f;
                float halfLength = Mathf.Max(particle.Diameter, particle.Length) * .5f;
                float normalAxisScale = radius / halfLength;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, particle.Axis);
                int start = _positions.Count;
                for (int i = 0; i < normals.Length; i++)
                {
                    // Stretch a sphere only along its travel axis, with no cylindrical section.
                    Vector3 unit = normals[i];
                    Vector3 local = new Vector3(unit.x * radius, unit.y * halfLength, unit.z * radius);
                    // Inverse-transpose normal for nonuniform scaling keeps gloss and edge fading correct.
                    Vector3 normal = new Vector3(unit.x, unit.y * normalAxisScale, unit.z).normalized;
                    _positions.Add(particle.Center + rotation * local);
                    _normals.Add(rotation * normal);
                    _colors.Add(particle.Color);
                    _surfaces.Add(particle.Surface);
                    _glowUvs.Add(Vector2.zero);
                }
                // All indices stay inside one stretched sphere. Ghosts are independent closed meshes.
                for (int i = 0; i < triangles.Length; i++) _indices.Add(start + triangles[i]);
                if (particle.Surface.z > 0) AddGlow(particle, cameraRight, cameraUp, cameraForward);
            }
            private void AddGlow(DrawParticle particle, Vector3 right, Vector3 up, Vector3 forward)
            {
                // Only the light halo faces the camera; the solid particle remains a 3D ellipsoid.
                float extent = Mathf.Max(particle.Diameter, particle.Length * .5f) * 3f;
                int start = _positions.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float x = corner == 1 || corner == 2 ? 1 : -1;
                    float y = corner >= 2 ? 1 : -1;
                    _positions.Add(particle.Center + (right * x + up * y) * extent);
                    _normals.Add(-forward);
                    _colors.Add(particle.Color);
                    _surfaces.Add(new Vector4(0, 0, particle.Surface.z, 1));
                    _glowUvs.Add(new Vector2(x, y));
                }
                _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 1);
                _indices.Add(start); _indices.Add(start + 3); _indices.Add(start + 2);
            }
            public void Upload(Mesh mesh)
            {
                mesh.Clear();
                if (_positions.Count == 0) return;
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetUVs(0, _glowUvs);
                mesh.SetUVs(1, _surfaces);
                mesh.SetTriangles(_indices, 0, false);
                mesh.RecalculateBounds();
            }
        }

        public LiquidRenderer(string name, MilkPerformanceLog performance = null)
        {
            _performance = performance;
            BuildSphere(out _normals, out _triangles);
            _buffer = new MeshBuffer(_normals.Length, _triangles.Length);
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainGameMilkSpray.milkliquid.bundle"))
                {
                    if (stream == null) throw new InvalidOperationException("Embedded liquid shader bundle is missing.");
                    byte[] bytes = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) throw new EndOfStreamException("Incomplete liquid shader bundle.");
                        offset += read;
                    }
                    _bundle = AssetBundle.LoadFromMemory(bytes);
                }
                Shader shader = _bundle != null ? _bundle.LoadAsset<Shader>("assets/milkliquid.shader") : null;
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Embedded liquid shader could not be loaded or is unsupported.");
                _material = new Material(shader) { name = "Milk.StretchedSpheres", hideFlags = HideFlags.HideAndDontSave };
                _mesh = new Mesh { name = "Milk.StretchedSpheresAndAfterimages", indexFormat = IndexFormat.UInt32 };
                _mesh.MarkDynamic();
                _root = new GameObject("MainGameMilkSpray." + name);
                UnityEngine.Object.DontDestroyOnLoad(_root);
                _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _root.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = _material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.enabled = false;
                // Reuse the combined mesh and sort for the camera actually about to render.
                Camera.onPreCull += BeforeCameraCull;
            }
            catch { Dispose(); throw; }
        }

        public void Render(LiquidSimulation simulation)
        {
            _count = 0;
            int drops = 0, ghosts = 0;
            foreach (var d in simulation.Drops)
            {
                if (!d.Active) continue;
                drops++;
                float fade = d.Fade(simulation.Now);
                Add(U(d.Position(simulation.Now)), U(d.Launch.AxisAt(simulation.Now, d.Spec.Gravity)),
                    d.Size * fade, d.Length * fade, ColorOf(d.Spec), d.Spec);
            }
            foreach (var g in simulation.Ghosts)
            {
                if (!g.Active) continue;
                var d = simulation.Drops[g.OwnerSlot];
                if (!d.Active || d.Id != g.OwnerId) continue;
                ghosts++;
                float age = Mathf.Clamp01((float)((simulation.Now - g.Born) / d.Spec.GhostLife));
                float scale = Mathf.Lerp(1, d.Spec.GhostEndScale, age);
                Color color = ColorOf(d.Spec);
                color.a *= d.Spec.GhostOpacity * (1 - age) * (1 - age);
                Add(U(g.Position), U(g.Axis), g.Size * scale, g.Length * scale, color, d.Spec);
            }
            _renderer.enabled = _count != 0;
            if (_count == 0) _mesh.Clear();
            _performance?.SetParticles(drops, ghosts, _count);
        }
        private void Add(Vector3 center, Vector3 axis, float diameter, float length, Color color, LiquidSpec spec)
        {
            if (diameter <= .000002f || color.a <= 0) return;
            _particles[_count] = new DrawParticle {
                Center = center, Axis = axis, Diameter = diameter, Length = Mathf.Max(diameter, length),
                Color = color, Surface = new Vector4(spec.Gloss, spec.EdgeSoftness, spec.Emission, 0), Order = _count
            };
            _count++;
        }
        private void BeforeCameraCull(Camera camera)
        {
            if (_disposed || _count == 0 || camera == null || (camera.cullingMask & (1 << _root.layer)) == 0) return;
            _performance?.TouchFrame(Time.frameCount);
            int cameraId = 0;
            string cameraName = null;
            if (_performance != null && _performance.Capturing)
            {
                cameraId = camera.GetInstanceID();
                if (!_cameraNames.TryGetValue(cameraId, out cameraName))
                {
                    if (_cameraNames.Count >= 64) _cameraNames.Clear();
                    _cameraNames[cameraId] = cameraName = camera.name;
                }
            }
            MilkPerformanceLog.CameraSample sample = _performance != null ? _performance.BeginCamera() : default;
            try
            {
                Vector3 position = camera.transform.position, forward = camera.transform.forward;
                Vector3 right = camera.transform.right, up = camera.transform.up;
                using (MilkPerformanceLog.Measure(_performance, MilkStage.Sort))
                {
                    for (int i = 0; i < _count; i++)
                        _particles[i].Depth = Vector3.Dot(_particles[i].Center - position, forward);
                    SortByDepth();
                }
                using (MilkPerformanceLog.Measure(_performance, MilkStage.Geometry))
                {
                    _buffer.Clear();
                    for (int i = 0; i < _count; i++)
                        _buffer.Add(_particles[i], _normals, _triangles, right, up, forward);
                }
                using (MilkPerformanceLog.Measure(_performance, MilkStage.Upload)) _buffer.Upload(_mesh);
            }
            finally { _performance?.EndCamera(sample, cameraId, cameraName, _count, _buffer.VertexCount, _buffer.TriangleCount); }
        }
        private static int Compare(DrawParticle a, DrawParticle b)
        {
            int depth = b.Depth.CompareTo(a.Depth);
            return depth != 0 ? depth : a.Order.CompareTo(b.Order);
        }
        private void SortByDepth()
        {
            // In-place heapsort: no temporary arrays, boxing or per-camera comparison delegates.
            for (int root = _count / 2 - 1; root >= 0; root--) SiftDown(root, _count);
            for (int end = _count - 1; end > 0; end--)
            {
                DrawParticle first = _particles[0];
                _particles[0] = _particles[end]; _particles[end] = first;
                SiftDown(0, end);
            }
        }
        private void SiftDown(int root, int length)
        {
            DrawParticle value = _particles[root];
            int child;
            while ((child = root * 2 + 1) < length)
            {
                if (child + 1 < length && Compare(_particles[child], _particles[child + 1]) < 0) child++;
                if (Compare(value, _particles[child]) >= 0) break;
                _particles[root] = _particles[child];
                root = child;
            }
            _particles[root] = value;
        }
        private static Color ColorOf(LiquidSpec s) => new Color(s.Red, s.Green, s.Blue, s.Alpha);
        internal static Vector3 U(V3 v) => new Vector3(v.X, v.Y, v.Z);
        internal static V3 V(Vector3 v) => new V3(v.x, v.y, v.z);

        private static void BuildSphere(out Vector3[] normals, out int[] triangles)
        {
            const int rings = Latitudes - 1;
            normals = new Vector3[2 + rings * Longitudes];
            triangles = new int[Longitudes * rings * 6];
            normals[0] = Vector3.up;
            int bottom = normals.Length - 1;
            normals[bottom] = Vector3.down;
            for (int row = 0; row < rings; row++)
            {
                float theta = Mathf.PI * (row + 1) / Latitudes;
                bool equator = row + 1 == Latitudes / 2;
                float ringRadius = equator ? 1 : Mathf.Sin(theta);
                float y = equator ? 0 : Mathf.Cos(theta);
                for (int column = 0; column < Longitudes; column++)
                {
                    float phi = Mathf.PI * 2 * column / Longitudes;
                    int index = 1 + row * Longitudes + column;
                    normals[index] = new Vector3(ringRadius * Mathf.Cos(phi), y, ringRadius * Mathf.Sin(phi));
                }
            }
            int at = 0;
            for (int column = 0; column < Longitudes; column++)
            {
                int next = (column + 1) % Longitudes;
                triangles[at++] = 0; triangles[at++] = 1 + next; triangles[at++] = 1 + column;
                for (int row = 0; row < rings - 1; row++)
                {
                    int a = 1 + row * Longitudes + column, b = 1 + row * Longitudes + next;
                    int c = a + Longitudes, d = b + Longitudes;
                    triangles[at++] = a; triangles[at++] = b; triangles[at++] = c;
                    triangles[at++] = b; triangles[at++] = d; triangles[at++] = c;
                }
                int last = 1 + (rings - 1) * Longitudes;
                triangles[at++] = last + column; triangles[at++] = last + next; triangles[at++] = bottom;
            }
        }
        public void Clear()
        {
            _count = 0;
            if (_renderer != null) _renderer.enabled = false;
            if (_mesh != null) _mesh.Clear();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Camera.onPreCull -= BeforeCameraCull;
            Clear();
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_material);
            if (_bundle != null) _bundle.Unload(true);
        }
    }
}

