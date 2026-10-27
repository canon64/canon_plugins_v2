using UnityEngine;

namespace MainGameDisplacementMap
{
    public sealed partial class Plugin
    {
        private sealed class OverlayDrawer : MonoBehaviour
        {
            internal Plugin Owner;
            internal Camera DrawCamera;
            private void OnPostRender()
            {
                if (Owner != null && Owner._fileLog != null && Owner._fileLog.Enabled
                    && Time.unscaledTime - Plugin._lastOnPostRenderLogTime > 1f)
                {
                    Owner._fileLog.Write("INFO", $"OverlayDrawer.OnPostRender entered cam={DrawCamera?.name}");
                    Plugin._lastOnPostRenderLogTime = Time.unscaledTime;
                }
                Owner?.DrawOnPostRender(DrawCamera);
            }
        }

        private Mesh _mesh;
        private Material _mat;
        private RenderTexture _overlayMaterialTexture;
        private float _overlayMaterialOpacity = float.NaN;
        private int _meshW, _meshH, _meshGX = -1, _meshGY = -1;
        private Vector2[] _meshUvs;
        private Vector2[] _meshBaseUvs;
        private Vector2[] _meshRadialDirections;
        private float[] _meshRadialMasks;
        private bool _meshRadialCacheValid;
        private bool _meshUvAtBase = true;
        private float _meshCacheCenterX, _meshCacheCenterY, _meshCacheRadius, _meshCacheInner, _meshCacheFalloff;

        private Texture2D _diagWhiteTex;

        // Visibility test (temporary, to prove OnPostRender draw actually reaches final screen)
        private Mesh _testQuadMesh;
        private Material _testMat;
        private int _testQuadW, _testQuadH;

        private void DrawOnPostRender(Camera cam)
        {
            if (!_cfgEnabled.Value)
            {
                _fileLog?.Write("WARN", "DrawOnPostRender skipped: disabled");
                return;
            }

            if (_rt == null)
            {
                _fileLog?.Write("WARN", "DrawOnPostRender skipped: rt null");
                return;
            }

            if (_mat == null)
            {
                _fileLog?.Write("WARN", "DrawOnPostRender skipped: mat null");
                return;
            }

            int w = (cam != null) ? cam.pixelWidth : Screen.width;
            int h = (cam != null) ? cam.pixelHeight : Screen.height;

            // === 生産モード判定 ===
            // 強制カメラインデックス使用中は、個別診断configがtrueでも描画・詳細ログを一切出さない
            bool usingForcedCamera = _cfgDiagnosticCameraIndex != null && _cfgDiagnosticCameraIndex.Value > 0;

            if (usingForcedCamera)
            {
                // 完全に通常波紋描画モードとして扱う
                goto NormalDisplacement;
            }

            // === 以下は診断configが明示的にtrueの時だけ動く（通常運用では到達しない） ===
            if (_cfgDiagnosticRTPreview != null && _cfgDiagnosticRTPreview.Value)
            {
                if (Time.unscaledTime - Plugin._lastDiagnosticRTLogTime > 1f)
                {
                    _fileLog?.Write("INFO", $"Diagnostic RT preview drawn on {cam?.name}");
                    Plugin._lastDiagnosticRTLogTime = Time.unscaledTime;
                }
                return;
            }

            if (_cfgDiagnosticOverlay != null && _cfgDiagnosticOverlay.Value)
            {
                if (Time.unscaledTime - Plugin._lastDiagnosticLogTime > 1f)
                {
                    _fileLog?.Write("INFO", $"Diagnostic overlay drawn on {cam?.name}");
                    Plugin._lastDiagnosticLogTime = Time.unscaledTime;
                }
                return;
            }

        NormalDisplacement:

            EnsureMesh(w, h);

            if (_mesh == null)
            {
                _fileLog?.Write("WARN", "DrawOnPostRender skipped: mesh still null after EnsureMesh");
                return;
            }

            UpdateUvForDisplacement();

            if (_overlayMaterialTexture != _rt)
            {
                _mat.mainTexture = _rt;
                _overlayMaterialTexture = _rt;
            }

            float opacity = _cfgOpacity.Value;
            if (IsExternalActiveNow())
                opacity = _externalOpacity * _externalIntensity;

            opacity = Mathf.Clamp01(opacity);
            if (_overlayMaterialOpacity != opacity)
            {
                _mat.color = new Color(1f, 1f, 1f, opacity);
                _overlayMaterialOpacity = opacity;
            }

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            try
            {
                _mat.SetPass(0);
                Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
            }
            finally
            {
                GL.PopMatrix();
            }

            if (_fileLog.Enabled) _fileLog.Write("INFO", $"DrawOnPostRender: mesh drawn on {cam?.name} depth={cam?.depth}");
        }

        private void EnsureMaterial()
        {
            if (_mat != null) return;

            Shader s = Shader.Find("Unlit/Transparent")
                    ?? Shader.Find("Unlit/Texture")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("UI/Default")
                    ?? Shader.Find("GUI/Text Shader");

            if (s != null)
            {
                _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                if (_fileLog != null) _fileLog.Write("INFO", "Material created with shader: " + s.name);
            }
            else
            {
                if (_fileLog != null) _fileLog.Write("ERROR", "NO SUITABLE SHADER FOUND for overlay");
            }
        }

        private void EnsureVisibilityTestMaterial()
        {
            if (_testMat != null) return;

            Shader s = Shader.Find("Unlit/Transparent")
                    ?? Shader.Find("Unlit/Texture")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("UI/Default")
                    ?? Shader.Find("GUI/Text Shader");

            if (s == null) return;

            _testMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };

            // Force states that should make the quad survive depth/post-process
            _testMat.SetFloat("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _testMat.SetFloat("_ZWrite", 0f);
            // Blend SrcAlpha OneMinusSrcAlpha is expected from the Transparent variant
        }

        private void EnsureVisibilityTestQuad(int w, int h)
        {
            if (_testQuadMesh != null && _testQuadW == w && _testQuadH == h) return;

            if (_testQuadMesh != null) Destroy(_testQuadMesh);

            _testQuadW = w;
            _testQuadH = h;

            // Dead-simple full-screen quad (2 triangles)
            _testQuadMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "VisibilityTestQuad" };
            _testQuadMesh.vertices = new Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(w, 0f, 0f),
                new Vector3(0f, h, 0f),
                new Vector3(w, h, 0f)
            };
            _testQuadMesh.uv = new Vector2[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f)
            };
            _testQuadMesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            _testQuadMesh.RecalculateBounds();
        }

        private void EnsureMesh(int w, int h)
        {
            int gx = Mathf.Clamp(_cfgGridX.Value, 4, 256);
            int gy = Mathf.Clamp(_cfgGridY.Value, 4, 256);

            if (_mesh != null && _meshW == w && _meshH == h && _meshGX == gx && _meshGY == gy) return;

            if (_mesh != null) Destroy(_mesh);

            _meshW = w; _meshH = h; _meshGX = gx; _meshGY = gy;

            int vx = gx + 1, vy = gy + 1;
            Vector3[] verts = new Vector3[vx * vy];
            Vector2[] uvs = new Vector2[vx * vy];
            int[] tris = new int[gx * gy * 6];

            int n = 0;
            for (int y = 0; y <= gy; y++)
            {
                for (int x = 0; x <= gx; x++)
                {
                    float nx = x / (float)gx;
                    float ny = y / (float)gy;
                    verts[n] = new Vector3(w * nx, h * ny, 0f);
                    uvs[n++] = new Vector2(nx, 1f - ny);
                }
            }

            int t = 0;
            for (int y = 0; y < gy; y++)
            {
                for (int x = 0; x < gx; x++)
                {
                    int i0 = y * (gx + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + gx + 1;
                    int i3 = i2 + 1;
                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            _mesh = new Mesh
            {
                name = PluginName + "_Grid",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = verts,
                uv = uvs,
                triangles = tris
            };
            _meshUvs = uvs;
            _meshBaseUvs = (Vector2[])uvs.Clone();
            _meshUvAtBase = true;
            _meshRadialDirections = new Vector2[uvs.Length];
            _meshRadialMasks = new float[uvs.Length];
            _meshRadialCacheValid = false;
            _mesh.RecalculateBounds();
        }

        private void EnsureMeshRadialCache()
        {
            float cx = _cfgCenterX.Value;
            float cy = _cfgCenterY.Value;
            float radius = Mathf.Max(0.0001f, _cfgRadius.Value);
            float inner = Mathf.Clamp(_cfgInner.Value, 0f, radius);
            float falloff = Mathf.Max(0.001f, _cfgFalloff.Value);
            if (_meshRadialCacheValid && _meshCacheCenterX == cx && _meshCacheCenterY == cy
                && _meshCacheRadius == radius && _meshCacheInner == inner && _meshCacheFalloff == falloff)
                return;

            _meshCacheCenterX = cx;
            _meshCacheCenterY = cy;
            _meshCacheRadius = radius;
            _meshCacheInner = inner;
            _meshCacheFalloff = falloff;
            _meshRadialCacheValid = true;
            Vector2 center = new Vector2(cx, cy);
            for (int i = 0; i < _meshBaseUvs.Length; i++)
            {
                Vector2 d = _meshBaseUvs[i] - center;
                float r = d.magnitude;
                if (r > radius)
                {
                    _meshRadialMasks[i] = 0f;
                    continue;
                }
                _meshRadialDirections[i] = r > 0.000001f ? d / r : Vector2.right;
                _meshRadialMasks[i] = Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp(radius, inner, r)), falloff);
            }
        }

        private void ReleaseMeshAndMat()
        {
            if (_mesh != null) { Destroy(_mesh); _mesh = null; }
            _meshUvs = null;
            _meshBaseUvs = null;
            _meshUvAtBase = true;
            _meshRadialDirections = null;
            _meshRadialMasks = null;
            _meshRadialCacheValid = false;
            if (_mat != null) { Destroy(_mat); _mat = null; }
            _overlayMaterialTexture = null;
            _overlayMaterialOpacity = float.NaN;
            _meshGX = _meshGY = -1;

            if (_testQuadMesh != null) { Destroy(_testQuadMesh); _testQuadMesh = null; }
            if (_testMat != null) { Destroy(_testMat); _testMat = null; }
            _testQuadW = _testQuadH = 0;
        }

        private void UpdateUvForDisplacement()
        {
            if (_mesh == null) return;

            float strength = _cfgStrength.Value * _cfgPulse.Value * 2.2f;
            if (strength == 0f)
            {
                if (!_meshUvAtBase)
                {
                    System.Array.Copy(_meshBaseUvs, _meshUvs, _meshUvs.Length);
                    _mesh.uv = _meshUvs;
                    _meshUvAtBase = true;
                }
                return;
            }

            UpdateDisplacementMapIfNeeded();
            EnsureMeshRadialCache();

            Vector2[] uvs = _meshUvs;
            if (uvs == null) return;
            float scrollX = _cfgMapScrollX.Value * Time.time * 0.15f;
            float scrollY = _cfgMapScrollY.Value * Time.time * 0.15f;
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 baseUv = _meshBaseUvs[i];
                float mask = _meshRadialMasks[i];
                if (mask <= 0f) { uvs[i] = baseUv; continue; }
                Vector2 disp = SampleDisplacementMap(baseUv, scrollX, scrollY);
                float scale = strength * mask;
                uvs[i] = baseUv + disp * scale
                    + _meshRadialDirections[i] * disp.magnitude * scale * 0.35f;
            }
            _mesh.uv = uvs;
            _meshUvAtBase = false;
        }

        // 実装は Plugin.Displacement.cs に移動 (マップ駆動版)
    }
}
