using System;
using System.Collections.Generic;
using CanonPlugins.Shared;
using MainGameSubCameraDisplayProbe;
using UnityEngine;

namespace MainGameKaleidoscope
{
    public sealed partial class Plugin
    {
        private const int CaptureLayer = 30;
        private const string DildoModelName = "VR Vibe (weighted FK/IK)";

        private sealed class ScreenDrawer : MonoBehaviour
        {
            internal Plugin Owner;
            internal Camera WorldCamera;
            private void OnRenderImage(RenderTexture source, RenderTexture destination)
            {
                if (Owner == null) { Graphics.Blit(source, destination); return; }
                Owner.DrawScreen(source, destination, WorldCamera);
            }
        }

        private struct LayerSwap { internal GameObject Object; internal int Layer; }
        private struct LightSwap { internal Light Light; internal int Mask; }
        private struct RendererSwap { internal Renderer Renderer; internal bool Enabled; }

        private readonly List<LayerSwap> _layerSwaps = new List<LayerSwap>(512);
        private readonly List<LightSwap> _lightSwaps = new List<LightSwap>(64);
        private readonly List<RendererSwap> _rendererSwaps = new List<RendererSwap>(256);
        private readonly Dictionary<ChaControl, Renderer[]> _rendererCache = new Dictionary<ChaControl, Renderer[]>();
        private GameObject _dildoModel;
        private Renderer[] _dildoRenderers = new Renderer[0];
        private float _nextDildoScan;
        private ChaControl[] _characters = new ChaControl[0];
        private Light[] _lights = new Light[0];
        private float _nextCharacterScan;
        private float _nextLightScan;
        private float _nextRendererRefresh;
        private Camera _worldCamera;
        private ScreenDrawer _drawer;
        private GameObject _sourceCameraObject, _foregroundCameraObject;
        private Camera _sourceCaptureCamera, _foregroundCaptureCamera;
        private RenderTexture _femaleTexture, _foregroundTexture, _afterimageTexture;
        private Material _screenMaterial;
        private string _screenStatus = "待機中";
        private int _lastScreenErrorFrame = -1;

        private void UpdateScreenEffect(bool visible)
        {
            if (!visible)
            {
                if (_drawer != null) DetachScreenDrawer();
                _screenStatus = "待機中";
                return;
            }

            Camera camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
            {
                _screenStatus = "メインカメラ待ち";
                DetachScreenDrawer();
                return;
            }
            if (_worldCamera == camera && _drawer != null) return;
            DetachScreenDrawer();
            _worldCamera = camera;
            _drawer = camera.gameObject.AddComponent<ScreenDrawer>();
            _drawer.Owner = this;
            _drawer.WorldCamera = camera;
        }

        private void DrawScreen(RenderTexture original, RenderTexture destination, Camera worldCamera)
        {
            if (_strength <= .0001f || _shader == null || worldCamera == null)
            {
                Graphics.Blit(original, destination);
                return;
            }

            try
            {
                if (!MainGameSubCameraDisplayProbeApi.TryGetKaleidoscopeSource(
                    out Camera subCamera, out ChaControl calledActor, out string reason))
                {
                    _screenStatus = "サブカメラ待ち: " + reason;
                    Graphics.Blit(original, destination);
                    return;
                }
                SimpleAfterimage.Plugin.SetKaleidoscopeThoughtMask(true, calledActor);
                if (!EnsureScreenResources(subCamera, original))
                {
                    _screenStatus = "描画素材の準備待ち";
                    Graphics.Blit(original, destination);
                    return;
                }

                ScanCharacters();
                // A leased called actor owns the source shot. It is deliberately excluded
                // from the main-camera foreground while the lease remains valid.
                if (!CaptureCharacters(_sourceCaptureCamera, subCamera, _femaleTexture, true, calledActor))
                {
                    _screenStatus = calledActor != null ? "呼び出しキャラ待ち" : "女性キャラ待ち";
                    Graphics.Blit(original, destination);
                    return;
                }
                CaptureCharacters(_foregroundCaptureCamera, worldCamera, _foregroundTexture, false, calledActor);
                RenderTexture previousTarget = RenderTexture.active;
                Graphics.SetRenderTarget(_afterimageTexture);
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = previousTarget;
                SimpleAfterimage.Plugin.DrawAfterComposite(worldCamera, _afterimageTexture,
                    original.width, original.height, 1f);
                ApplyScreenParameters();
                Graphics.Blit(original, destination, _screenMaterial);
                _screenStatus = calledActor != null
                    ? (_sourceIncludeBackground.Value ? "呼び出しキャラ＋サブカメラ背景を表示（キャラは前景から除外）" : "呼び出しキャラを万華鏡に表示（前景から除外）")
                    : (_sourceIncludeBackground.Value ? "女性＋サブカメラ背景を表示" : "サブカメラの女性を万華鏡に表示");
            }
            catch (Exception ex)
            {
                if (_lastScreenErrorFrame < 0 || Time.frameCount - _lastScreenErrorFrame > 300)
                {
                    Logger.LogWarning("Kaleidoscope screen capture failed: " + ex);
                    _lastScreenErrorFrame = Time.frameCount;
                }
                _screenStatus = "描画エラー: " + ex.Message;
                Graphics.Blit(original, destination);
            }
        }

        private bool EnsureScreenResources(Camera subCamera, RenderTexture original)
        {
            if (_screenMaterial == null)
                _screenMaterial = new Material(_shader) { name = "KaleidoscopeScreenComposite", hideFlags = HideFlags.HideAndDontSave };
            if (_sourceCaptureCamera == null)
            {
                _sourceCameraObject = new GameObject("__KaleidoscopeFemaleCapture");
                _sourceCameraObject.hideFlags = HideFlags.HideAndDontSave;
                _sourceCaptureCamera = _sourceCameraObject.AddComponent<Camera>();
                _sourceCaptureCamera.enabled = false;
            }
            if (_foregroundCaptureCamera == null)
            {
                _foregroundCameraObject = new GameObject("__KaleidoscopeForegroundCapture");
                _foregroundCameraObject.hideFlags = HideFlags.HideAndDontSave;
                _foregroundCaptureCamera = _foregroundCameraObject.AddComponent<Camera>();
                _foregroundCaptureCamera.enabled = false;
            }
            if (subCamera.targetTexture == null) return false;
            // The shader samples source UVs across the screen. Give the source
            // the same aspect as the destination so subjects are not stretched
            // when the subcamera uses a different render resolution.
            float quality = _sourceQuality.Value;
            float scale = Mathf.Min(quality, 2048f / Mathf.Max(original.width, original.height));
            scale = Mathf.Max(scale, 128f / Mathf.Min(original.width, original.height));
            int sourceW = Mathf.RoundToInt(original.width * scale);
            int sourceH = Mathf.RoundToInt(original.height * scale);
            EnsureTexture(ref _femaleTexture, sourceW, sourceH, "KaleidoscopeFemaleRT",
                subCamera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32);
            EnsureTexture(ref _foregroundTexture, original.width, original.height, "KaleidoscopeForegroundRT");
            EnsureTexture(ref _afterimageTexture, original.width, original.height, "KaleidoscopeAfterimageRT");
            return _femaleTexture != null && _foregroundTexture != null && _afterimageTexture != null;
        }

        private static void EnsureTexture(ref RenderTexture target, int width, int height, string name,
            RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            if (target != null && target.width == width && target.height == height &&
                target.format == format && target.IsCreated()) return;
            if (target != null) { target.Release(); Destroy(target); }
            target = new RenderTexture(width, height, 24, format)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            target.Create();
        }

        private void ScanCharacters()
        {
            if (Time.unscaledTime >= _nextCharacterScan)
            {
                _characters = FindObjectsOfType<ChaControl>();
                _nextCharacterScan = Time.unscaledTime + .25f;
            }
            if (Time.unscaledTime >= _nextLightScan)
            {
                _lights = FindObjectsOfType<Light>();
                _nextLightScan = Time.unscaledTime + .75f;
            }
            if (Time.unscaledTime >= _nextRendererRefresh)
            {
                _rendererCache.Clear();
                if (_dildoModel != null)
                    _dildoRenderers = _dildoModel.GetComponentsInChildren<Renderer>(true);
                _nextRendererRefresh = Time.unscaledTime + .5f;
            }
            if (_dildoModel == null && Time.unscaledTime >= _nextDildoScan)
            {
                _dildoModel = GameObject.Find(DildoModelName);
                _dildoRenderers = _dildoModel != null
                    ? _dildoModel.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                _nextDildoScan = Time.unscaledTime + .25f;
            }
        }

        private bool CaptureCharacters(Camera capture, Camera reference, RenderTexture target,
            bool sourceCapture, ChaControl calledActor)
        {
            _layerSwaps.Clear();
            _lightSwaps.Clear();
            _rendererSwaps.Clear();
            int sourceLayers = 0;
            int selected = 0;
            try
            {
                for (int i = 0; i < _characters.Length; i++)
                {
                    ChaControl character = _characters[i];
                    if (character == null || !character.gameObject.activeInHierarchy) continue;
                    bool include = sourceCapture
                        ? character.visibleAll && (calledActor != null ? character == calledActor : character.sex == 1)
                        : character.visibleAll && character != calledActor;
                    bool hideForCapture = !sourceCapture && _thoughtActive &&
                        character == calledActor;
                    if (!include && !hideForCapture && !(sourceCapture && _sourceIncludeBackground.Value)) continue;
                    if (!_rendererCache.TryGetValue(character, out Renderer[] renderers))
                    {
                        renderers = character.GetComponentsInChildren<Renderer>(true);
                        _rendererCache[character] = renderers;
                    }
                    if (!include)
                    {
                        // In background mode, keep the room while hiding characters that
                        // are not the selected source subject for this one capture.
                        foreach (Renderer renderer in renderers)
                        {
                            if (renderer == null || !renderer.enabled) continue;
                            _rendererSwaps.Add(new RendererSwap { Renderer = renderer, Enabled = true });
                            renderer.enabled = false;
                        }
                        continue;
                    }
                    selected++;
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        GameObject go = renderer.gameObject;
                        int previousLayer = go.layer;
                        sourceLayers |= 1 << previousLayer;
                        if (previousLayer == CaptureLayer) continue;
                        _layerSwaps.Add(new LayerSwap { Object = go, Layer = previousLayer });
                        go.layer = CaptureLayer;
                    }
                }
                // The dildo is a standalone GameObject, outside every ChaControl.
                // Include it in the foreground mask and exclude it from a full
                // subcamera background capture so it remains in front of the effect.
                if (_dildoModel != null && _dildoModel.activeInHierarchy)
                {
                    foreach (Renderer renderer in _dildoRenderers)
                    {
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        if (sourceCapture)
                        {
                            if (_sourceIncludeBackground.Value)
                            {
                                _rendererSwaps.Add(new RendererSwap { Renderer = renderer, Enabled = true });
                                renderer.enabled = false;
                            }
                            continue;
                        }
                        GameObject go = renderer.gameObject;
                        int previousLayer = go.layer;
                        sourceLayers |= 1 << previousLayer;
                        if (previousLayer == CaptureLayer) continue;
                        _layerSwaps.Add(new LayerSwap { Object = go, Layer = previousLayer });
                        go.layer = CaptureLayer;
                    }
                }
                if (sourceCapture && selected == 0 && !_sourceIncludeBackground.Value) return false;
                foreach (Light light in _lights)
                {
                    if (light == null || !light.isActiveAndEnabled) continue;
                    int mask = light.cullingMask;
                    if ((mask & sourceLayers) == 0 || (mask & (1 << CaptureLayer)) != 0) continue;
                    _lightSwaps.Add(new LightSwap { Light = light, Mask = mask });
                    light.cullingMask = mask | (1 << CaptureLayer);
                }
                capture.CopyFrom(reference);
                capture.enabled = false;
                capture.transform.SetPositionAndRotation(reference.transform.position, reference.transform.rotation);
                capture.worldToCameraMatrix = reference.worldToCameraMatrix;
                if (sourceCapture)
                {
                    // Keep the source vertical FOV but recalculate horizontal
                    // framing for the texture aspect used by the composite.
                    capture.aspect = (float)target.width / target.height;
                    capture.ResetProjectionMatrix();
                }
                else
                    capture.projectionMatrix = reference.projectionMatrix;
                capture.targetTexture = target;
                bool includeBackground = sourceCapture && _sourceIncludeBackground.Value;
                capture.cullingMask = includeBackground ? reference.cullingMask | (1 << CaptureLayer) : 1 << CaptureLayer;
                capture.clearFlags = includeBackground ? reference.clearFlags : CameraClearFlags.SolidColor;
                capture.backgroundColor = includeBackground ? reference.backgroundColor : Color.clear;
                capture.allowHDR = reference.allowHDR;
                capture.allowMSAA = reference.allowMSAA;
                capture.rect = new Rect(0, 0, 1, 1);
                capture.Render();
                capture.targetTexture = null;
                return true;
            }
            finally
            {
                foreach (RendererSwap swap in _rendererSwaps)
                    if (swap.Renderer != null) swap.Renderer.enabled = swap.Enabled;
                foreach (LightSwap swap in _lightSwaps)
                    if (swap.Light != null) swap.Light.cullingMask = swap.Mask;
                for (int i = _layerSwaps.Count - 1; i >= 0; i--)
                {
                    LayerSwap swap = _layerSwaps[i];
                    if (swap.Object != null) swap.Object.layer = swap.Layer;
                }
                _lightSwaps.Clear();
                _rendererSwaps.Clear();
                _layerSwaps.Clear();
            }
        }

        private void ApplyScreenParameters()
        {
            Material mat = _screenMaterial;
            mat.SetTexture("_SourceTex", _femaleTexture);
            mat.SetTexture("_ForegroundTex", _foregroundTexture);
            mat.SetTexture("_AfterimageTex", _afterimageTexture);
            mat.SetFloat("_Strength", _strength * _strengthLimit.Value);
            mat.SetFloat("_Segments", _segments.Value);
            mat.SetFloat("_Rotation", _rotationOffset.Value + _clock * _rotationSpeed.Value);
            mat.SetFloat("_Zoom", _zoom.Value);
            mat.SetFloat("_CenterX", _centerX.Value);
            mat.SetFloat("_CenterY", _centerY.Value);
            mat.SetFloat("_SourceZoom", _sourceZoom.Value);
            mat.SetFloat("_SourceCenterX", _sourceCenterX.Value);
            mat.SetFloat("_SourceCenterY", _sourceCenterY.Value);
            mat.SetFloat("_SourceRotation", _sourceRotationDegrees.Value * Mathf.Deg2Rad);
            mat.SetFloat("_ScreenAspect", (float)_femaleTexture.width / _femaleTexture.height);
            mat.SetFloat("_Hue", _hueOffset.Value + _clock * _hueSpeed.Value);
            mat.SetFloat("_Saturation", _saturation.Value);
            mat.SetFloat("_Brightness", _brightness.Value);
            mat.SetFloat("_Swirl", _swirl.Value);
            mat.SetFloat("_Wave", _wave.Value);
            mat.SetFloat("_WaveSpeed", _waveSpeed.Value);
            mat.SetFloat("_Repeat", _radialRepeat.Value);
            mat.SetFloat("_Chromatic", _chromatic.Value);
            mat.SetFloat("_VignetteStrength", _vignetteStrength.Value);
            mat.SetFloat("_VignetteInner", _vignetteInner.Value);
            mat.SetFloat("_VignetteOuter", _vignetteOuter.Value);
            mat.SetFloat("_VignetteCenterX", _vignetteCenterX.Value);
            mat.SetFloat("_VignetteCenterY", _vignetteCenterY.Value);
            mat.SetColor("_VignetteColor", new Color(_vignetteRed.Value,
                _vignetteGreen.Value, _vignetteBlue.Value, 1f));
            mat.SetColor("_BaseColor", new Color(_baseRed.Value, _baseGreen.Value, _baseBlue.Value, 1f));
        }

        private void DetachScreenDrawer()
        {
            if (_drawer != null) { _drawer.Owner = null; Destroy(_drawer); }
            _drawer = null;
            _worldCamera = null;
        }

        private void DisposeScreenEffect()
        {
            DetachScreenDrawer();
            if (_sourceCameraObject != null) Destroy(_sourceCameraObject);
            if (_foregroundCameraObject != null) Destroy(_foregroundCameraObject);
            _sourceCameraObject = null;
            _foregroundCameraObject = null;
            _sourceCaptureCamera = null;
            _foregroundCaptureCamera = null;
            if (_femaleTexture != null) { _femaleTexture.Release(); Destroy(_femaleTexture); _femaleTexture = null; }
            if (_foregroundTexture != null) { _foregroundTexture.Release(); Destroy(_foregroundTexture); _foregroundTexture = null; }
            if (_afterimageTexture != null) { _afterimageTexture.Release(); Destroy(_afterimageTexture); _afterimageTexture = null; }
            if (_screenMaterial != null) { Destroy(_screenMaterial); _screenMaterial = null; }
            _rendererCache.Clear();
            _dildoModel = null;
            _dildoRenderers = new Renderer[0];
        }

        private void OnDisable()
        {
            SimpleAfterimage.Plugin.SetKaleidoscopeThoughtMask(false, null);
            DisposeScreenEffect();
            RestoreAudioDucking();
        }
    }
}
