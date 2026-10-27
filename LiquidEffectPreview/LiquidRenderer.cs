using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiquidEffectPreview
{
    internal sealed class LiquidRenderer : IDisposable
    {
        private readonly GameObject _root;
        private readonly Mesh _jetMesh, _tailMesh;
        private readonly Material _jetMaterial, _dropMaterial, _tailMaterial;
        private readonly Texture2D _texture;
        private readonly ParticleSystem _particles;
        private readonly ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[LiquidSimulation.MaxDrops];
        private readonly TubeGeometry _jets = new TubeGeometry(), _tails = new TubeGeometry();
        private readonly List<PathPoint> _path = new List<PathPoint>(128);
        private readonly List<Vector3> _positions = new List<Vector3>(8192), _normals = new List<Vector3>(8192);
        private readonly List<Vector2> _uvs = new List<Vector2>(8192);
        private readonly List<Color> _colors = new List<Color>(8192);

        public LiquidRenderer(string side)
        {
            Shader solid = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            Shader soft = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
            if (solid == null || soft == null) throw new InvalidOperationException("Required liquid shaders are unavailable.");
            _root = new GameObject("LiquidEffectPreview." + side);
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _jetMaterial = new Material(solid) { name = "Liquid.Jet", hideFlags = HideFlags.HideAndDontSave };
            if (_jetMaterial.HasProperty("_Metallic")) _jetMaterial.SetFloat("_Metallic", 0);
            if (_jetMaterial.HasProperty("_Glossiness")) _jetMaterial.SetFloat("_Glossiness", .28f);
            _texture = CreateDropTexture();
            _dropMaterial = SoftMaterial(soft, "Liquid.Drop", _texture);
            _tailMaterial = SoftMaterial(soft, "Liquid.ShortTail", Texture2D.whiteTexture);
            _jetMesh = CreateMesh("Jets", _jetMaterial);
            _tailMesh = CreateMesh("ShortTails", _tailMaterial);

            var go = new GameObject("Drops"); go.transform.SetParent(_root.transform, false);
            _particles = go.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.playOnAwake = false; main.loop = true; main.maxParticles = LiquidSimulation.MaxDrops;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.simulationSpeed = 0;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = _particles.emission; emission.enabled = false;
            var shape = _particles.shape; shape.enabled = false;
            // Droplet trajectories and tails are supplied explicitly; native trail history is never used.
            var trails = _particles.trails; trails.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _dropMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            _particles.Play(); _particles.Pause();
        }

        private Mesh CreateMesh(string name, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(_root.transform, false);
            var mesh = new Mesh { name = "Liquid." + name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return mesh;
        }
        private static Material SoftMaterial(Shader shader, string name, Texture texture)
        {
            var material = new Material(shader) { name = name, mainTexture = texture, hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 2);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.EnableKeyword("_ALPHABLEND_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            return material;
        }
        private static Texture2D CreateDropTexture()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Liquid.SoftDrop", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - radius) * 6));
                float shade = Mathf.Clamp01(.86f + .14f * (1 - radius) + .08f * dy);
                texture.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        public void Render(LiquidSimulation simulation, PreviewSettings settings)
        {
            Color color = settings.Color.Value;
            _jetMaterial.color = color;
            // Standard's opaque surface gives the jets volume; user alpha is supported explicitly.
            bool transparent = color.a < .999f;
            if (_jetMaterial.HasProperty("_Mode")) _jetMaterial.SetFloat("_Mode", transparent ? 2 : 0);
            _jetMaterial.SetInt("_SrcBlend", (int)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            _jetMaterial.SetInt("_DstBlend", (int)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            _jetMaterial.SetInt("_ZWrite", transparent ? 0 : 1);
            if (transparent) _jetMaterial.EnableKeyword("_ALPHABLEND_ON"); else _jetMaterial.DisableKeyword("_ALPHABLEND_ON");
            _jetMaterial.renderQueue = transparent ? 3000 : 2000;
            _jets.Clear(); _tails.Clear();
            foreach (var burst in simulation.Bursts)
                if (burst.Active) for (int jet = 0; jet < burst.Spec.Jets; jet++)
                { simulation.JetPath(burst, jet, _path); _jets.AddPath(_path, true); }
            int count = 0;
            foreach (var drop in simulation.Drops)
            {
                if (!drop.Active) continue;
                float fade = drop.Fade(simulation.Now);
                _particleBuffer[count++] = new ParticleSystem.Particle {
                    position = U(drop.Position(simulation.Now)), startSize = drop.Size * fade,
                    startColor = new Color(color.r, color.g, color.b, color.a * fade), startLifetime = 1, remainingLifetime = 1,
                    randomSeed = (uint)drop.Id };
                if (settings.Trails.Value)
                {
                    simulation.DropTail(drop, settings.TrailLengthCm.Value * .01f, settings.TrailWidth.Value, _path);
                    _tails.AddPath(_path, false);
                }
            }
            Upload(_jetMesh, _jets, Color.white);
            Upload(_tailMesh, _tails, color);
            _particles.SetParticles(_particleBuffer, count);
        }
        private void Upload(Mesh mesh, TubeGeometry geometry, Color color)
        {
            _positions.Clear(); _normals.Clear(); _uvs.Clear(); _colors.Clear();
            foreach (var v in geometry.Vertices)
            {
                _positions.Add(U(v.Position)); _normals.Add(U(v.Normal)); _uvs.Add(new Vector2(v.U, v.V));
                _colors.Add(new Color(color.r, color.g, color.b, color.a * v.Alpha));
            }
            mesh.Clear();
            if (_positions.Count == 0) return;
            mesh.SetVertices(_positions); mesh.SetNormals(_normals); mesh.SetUVs(0, _uvs); mesh.SetColors(_colors);
            mesh.SetTriangles(geometry.Indices, 0); mesh.RecalculateBounds();
        }
        internal static Vector3 U(V3 v) => new Vector3(v.X, v.Y, v.Z);
        internal static V3 V(Vector3 v) => new V3(v.x, v.y, v.z);
        public void Clear() { _jetMesh.Clear(); _tailMesh.Clear(); _particles.Clear(); }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_jetMesh); UnityEngine.Object.Destroy(_tailMesh);
            UnityEngine.Object.Destroy(_jetMaterial); UnityEngine.Object.Destroy(_dropMaterial); UnityEngine.Object.Destroy(_tailMaterial);
            UnityEngine.Object.Destroy(_texture);
        }
    }
}
