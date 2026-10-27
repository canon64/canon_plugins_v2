using UnityEngine;
using UnityEngine.Rendering;

namespace MainGameVrScreenEffectProbe
{
    internal sealed class VrEyeScreenEffectLayer : MonoBehaviour
    {
        internal Plugin Owner;

        private sealed class EyeBuffer
        {
            internal RenderTexture History;
            internal RenderTexture Scratch;
            internal bool Blank = true;
            internal int Width;
            internal int Height;

            internal void Ensure(int width, int height)
            {
                if (History != null && Scratch != null && Width == width && Height == height)
                    return;

                Release();
                Width = width;
                Height = height;
                History = CreateTexture(width, height, "history");
                Scratch = CreateTexture(width, height, "scratch");
                Blank = true;
            }

            internal void Release()
            {
                ReleaseTexture(History);
                ReleaseTexture(Scratch);
                History = null;
                Scratch = null;
                Blank = true;
                Width = 0;
                Height = 0;
            }

            private static RenderTexture CreateTexture(int width, int height, string label)
            {
                var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "VrScreenEffectProbe_" + label + "_" + width + "x" + height,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                rt.Create();
                return rt;
            }

            private static void ReleaseTexture(RenderTexture rt)
            {
                if (rt == null)
                    return;
                rt.Release();
                Object.Destroy(rt);
            }
        }

        private readonly EyeBuffer _left = new EyeBuffer();
        private readonly EyeBuffer _right = new EyeBuffer();
        private readonly EyeBuffer _mono = new EyeBuffer();
        private Camera _camera;
        private Material _lineMaterial;
        private bool _lineMaterialMissingLogged;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();
            Owner?.Log(ProbeLogLevel.Debug, "layer enabled cam=" + (_camera != null ? _camera.name : "null"));
        }

        private void OnDisable()
        {
            Owner?.Log(ProbeLogLevel.Debug, "layer disabled cam=" + (_camera != null ? _camera.name : "null"));
        }

        private void OnDestroy()
        {
            ResetBuffers();
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
            Owner?.Log(ProbeLogLevel.Debug, "layer destroyed cam=" + (_camera != null ? _camera.name : "null"));
        }

        internal void ResetBuffers()
        {
            _left.Release();
            _right.Release();
            _mono.Release();
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (src == null || dst == null)
                return;

            Plugin owner = Owner;
            if (owner == null || !owner.RuntimeEnabled)
            {
                Graphics.Blit(src, dst);
                return;
            }

            if (_camera == null)
                _camera = GetComponent<Camera>();

            VrEyeKind eye = ResolveEye(_camera);
            owner.NoteRender(_camera, eye, src.width, src.height);

            EyeBuffer buffer = SelectBuffer(eye);
            buffer.Ensure(src.width, src.height);

            Graphics.Blit(src, dst);

            if (owner.AfterimageEnabled)
            {
                if (!buffer.Blank && buffer.History != null)
                    DrawTextureOver(dst, buffer.History, owner.AfterimageTint);
                UpdateHistory(src, buffer, owner.AfterimageFeedback);
            }

            if (owner.WaveEnabled)
                DrawWaveOverlay(dst, owner, eye);
        }

        private EyeBuffer SelectBuffer(VrEyeKind eye)
        {
            if (eye == VrEyeKind.Left)
                return _left;
            if (eye == VrEyeKind.Right)
                return _right;
            return _mono;
        }

        private static VrEyeKind ResolveEye(Camera cam)
        {
            if (cam == null)
                return VrEyeKind.Mono;

            if (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left)
                return VrEyeKind.Left;
            if (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right)
                return VrEyeKind.Right;

            if (cam.stereoTargetEye == StereoTargetEyeMask.Left)
                return VrEyeKind.Left;
            if (cam.stereoTargetEye == StereoTargetEyeMask.Right)
                return VrEyeKind.Right;

            string lower = (cam.name ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("left"))
                return VrEyeKind.Left;
            if (lower.Contains("right"))
                return VrEyeKind.Right;

            return VrEyeKind.Mono;
        }

        private static void UpdateHistory(RenderTexture src, EyeBuffer buffer, float feedback)
        {
            if (buffer.History == null || buffer.Scratch == null)
                return;

            if (buffer.Blank)
            {
                Graphics.Blit(src, buffer.History);
                buffer.Blank = false;
                return;
            }

            Graphics.Blit(src, buffer.Scratch);
            DrawTextureOver(buffer.Scratch, buffer.History, new Color(1f, 1f, 1f, Mathf.Clamp01(feedback)));
            Graphics.Blit(buffer.Scratch, buffer.History);
            buffer.Blank = false;
        }

        private static void DrawTextureOver(RenderTexture target, Texture texture, Color color)
        {
            if (target == null || texture == null || color.a <= 0.0001f)
                return;

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
                Graphics.DrawTexture(
                    new Rect(0f, 0f, target.width, target.height),
                    texture,
                    new Rect(0f, 0f, 1f, 1f),
                    0,
                    0,
                    0,
                    0,
                    color);
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private void DrawWaveOverlay(RenderTexture target, Plugin owner, VrEyeKind eye)
        {
            if (target == null || owner == null || owner.WaveAlpha <= 0.0001f)
                return;

            Material material = EnsureLineMaterial(owner);
            if (material == null)
                return;

            RenderTexture previous = RenderTexture.active;
            int width = target.width;
            int height = target.height;
            int segments = owner.WaveSegments;
            int rings = owner.WaveRingCount;
            Vector2 center01 = owner.WaveCenter;
            float centerX = center01.x * width;
            float centerY = center01.y * height;
            float maxRadius = Mathf.Max(width, height) * 0.75f;
            Color baseColor = eye == VrEyeKind.Right
                ? new Color(1f, 0.62f, 0.88f, 1f)
                : eye == VrEyeKind.Left
                    ? new Color(0.45f, 0.9f, 1f, 1f)
                    : new Color(0.75f, 1f, 0.82f, 1f);

            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, width, height, 0f);
                material.SetPass(0);
                GL.Begin(GL.LINES);

                for (int r = 0; r < rings; r++)
                {
                    float phase = Mathf.Repeat(Time.unscaledTime * owner.WaveSpeed + (float)r / rings, 1f);
                    float radius = Mathf.Lerp(8f, maxRadius, phase);
                    float alpha = owner.WaveAlpha * (1f - phase) * 0.9f;
                    if (alpha <= 0.0001f)
                        continue;

                    GL.Color(new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
                    for (int i = 0; i < segments; i++)
                    {
                        float a0 = Mathf.PI * 2f * i / segments;
                        float a1 = Mathf.PI * 2f * (i + 1) / segments;
                        GL.Vertex3(centerX + Mathf.Cos(a0) * radius, centerY + Mathf.Sin(a0) * radius, 0f);
                        GL.Vertex3(centerX + Mathf.Cos(a1) * radius, centerY + Mathf.Sin(a1) * radius, 0f);
                    }
                }

                GL.End();
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private Material EnsureLineMaterial(Plugin owner)
        {
            if (_lineMaterial != null)
                return _lineMaterial;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                if (!_lineMaterialMissingLogged)
                {
                    _lineMaterialMissingLogged = true;
                    owner.Log(ProbeLogLevel.Warning, "wave material shader missing: Hidden/Internal-Colored");
                }
                return null;
            }

            _lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);
            owner.Log(ProbeLogLevel.Debug, "wave material created");
            return _lineMaterial;
        }
    }
}
