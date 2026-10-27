using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameCharacterAfterimage
{
    internal sealed class LayeredAfterimagePipeline : IDisposable
    {
        private sealed class AfterimageSlot
        {
            internal RenderTexture Texture;
            internal int Life;
            internal long Serial;
        }

        private sealed class OverlayDrawer : MonoBehaviour
        {
            internal LayeredAfterimagePipeline Owner;

            private void OnPostRender()
            {
                Owner?.DrawOverlay();
            }
        }

        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarn;
        private readonly Action<string> _logDebug;

        private Plugin.PluginSettings _settings;
        private Camera _sourceCamera;
        private GameObject _root;
        private Camera _backgroundCamera;
        private Camera _overlayCamera;
        private Camera _captureCamera;
        private OverlayDrawer _overlayDrawer;
        private Material _overlayMaterial;
        private string _overlayShaderName;
        private int _sourceOriginalCullingMask;
        private CameraClearFlags _sourceOriginalClearFlags;
        private bool _sourceStateCaptured;

        private readonly List<AfterimageSlot> _activeSlots = new List<AfterimageSlot>(64);
        private AfterimageSlot[] _slots = Array.Empty<AfterimageSlot>();

        private int _characterMask;
        private float _nextLayerResolveTime;
        private bool _warnedNoCharacterLayer;
        private int _captureWidth;
        private int _captureHeight;
        private int _nextWriteSlot;
        private long _captureSerial;
        private int _frameCounter;
        private int _activeSlotCount;

        internal LayeredAfterimagePipeline(Action<string> logInfo, Action<string> logWarn, Action<string> logDebug)
        {
            _logInfo = logInfo ?? (_ => { });
            _logWarn = logWarn ?? (_ => { });
            _logDebug = logDebug ?? (_ => { });
        }

        internal Camera SourceCamera => _sourceCamera;

        internal bool IsBoundTo(Camera camera)
        {
            return _sourceCamera == camera;
        }

        internal bool OwnsCamera(Camera camera)
        {
            return camera != null
                && (camera == _backgroundCamera || camera == _overlayCamera || camera == _captureCamera);
        }

        internal void Bind(Camera sourceCamera, Plugin.PluginSettings settings)
        {
            _sourceCamera = sourceCamera;
            _settings = settings;
            _frameCounter = 0;
            _nextWriteSlot = 0;
            _captureSerial = 0;
            _warnedNoCharacterLayer = false;
            _nextLayerResolveTime = 0f;
            _characterMask = 0;
            _sourceStateCaptured = false;

            EnsureCameras();
            CaptureSourceState();
            ResolveCharacterMask(force: true);
            EnsureOverlayMaterial();
            EnsureSlotTextures();
            ClearSlots();

            _logInfo("pipeline bound to camera: " + _sourceCamera.name);
        }

        internal void UpdateSettings(Plugin.PluginSettings settings)
        {
            _settings = settings;
            EnsureOverlayMaterial();
            EnsureSlotTextures();
            ResolveCharacterMask(force: false);
        }

        internal void Tick()
        {
            if (_settings == null || !_settings.Enabled)
            {
                _activeSlotCount = 0;
                return;
            }

            if (_sourceCamera == null || !_sourceCamera)
            {
                _activeSlotCount = 0;
                return;
            }

            EnsureCameras();
            CaptureSourceState();

            if (!ResolveCharacterMask(force: false))
            {
                DisableAuxCameras();
                RestoreSourceState();
                _activeSlotCount = 0;
                return;
            }

            EnsureOverlayMaterial();
            EnsureSlotTextures();
            SyncAuxCameras();

            AgeSlots();

            _frameCounter++;
            if (_settings.CaptureIntervalFrames <= 1 || (_frameCounter % _settings.CaptureIntervalFrames) == 0)
            {
                CaptureCharacterFrame();
            }

            _activeSlotCount = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Life > 0 && _slots[i].Texture != null)
                {
                    _activeSlotCount++;
                }
            }
        }

        internal void GetStats(out int activeSlots, out int totalSlots, out int captureWidth, out int captureHeight)
        {
            activeSlots = _activeSlotCount;
            totalSlots = _slots?.Length ?? 0;
            captureWidth = _captureWidth;
            captureHeight = _captureHeight;
        }

        internal void DrawOverlay()
        {
            if (_settings == null || !_settings.Enabled)
            {
                return;
            }

            if (_slots == null || _slots.Length == 0)
            {
                return;
            }

            _activeSlots.Clear();
            for (int i = 0; i < _slots.Length; i++)
            {
                AfterimageSlot slot = _slots[i];
                if (slot.Life > 0 && slot.Texture != null)
                {
                    _activeSlots.Add(slot);
                }
            }

            if (_activeSlots.Count == 0)
            {
                return;
            }

            SortSlotsByFadePriority(_activeSlots);

            float invFade = 1f / Mathf.Max(1, _settings.FadeFrames);
            Color tint = new Color(_settings.OverlayTintR, _settings.OverlayTintG, _settings.OverlayTintB, _settings.OverlayTintA);
            Rect rect = new Rect(0f, 0f, Screen.width, Screen.height);
            bool frontMode = _settings.OverlayInFrontOfCharacter;
            float frontAutoScale = 1f;

            if (frontMode)
            {
                float totalAlpha = 0f;
                for (int i = 0; i < _activeSlots.Count; i++)
                {
                    AfterimageSlot slot = _activeSlots[i];
                    if (slot.Life >= _settings.FadeFrames)
                    {
                        continue;
                    }

                    float life01 = Mathf.Clamp01(slot.Life * invFade);
                    float alpha = tint.a * _settings.AfterimageAlphaScale * life01 * life01;
                    if (alpha > 0.0001f)
                    {
                        totalAlpha += alpha;
                    }
                }

                float target = Mathf.Max(0.01f, _settings.FrontOverlayTargetTotalAlpha);
                if (totalAlpha > target && totalAlpha > 0.0001f)
                {
                    frontAutoScale = target / totalAlpha;
                }
            }

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            try
            {
                for (int i = 0; i < _activeSlots.Count; i++)
                {
                    AfterimageSlot slot = _activeSlots[i];
                    // Hide the newest frame (current silhouette) and show only trailing images.
                    if (slot.Life >= _settings.FadeFrames)
                    {
                        continue;
                    }

                    float life01 = Mathf.Clamp01(slot.Life * invFade);
                    float alpha = tint.a * _settings.AfterimageAlphaScale * life01 * life01 * frontAutoScale;
                    if (alpha <= 0.0001f)
                    {
                        continue;
                    }
                    Color c = new Color(tint.r, tint.g, tint.b, alpha);
                    Graphics.DrawTexture(rect, slot.Texture, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0, c);
                }
            }
            catch (Exception ex)
            {
                _logWarn("overlay draw failed: " + ex.Message);
            }
            finally
            {
                GL.PopMatrix();
            }
        }

        public void Dispose()
        {
            DisableAuxCameras();
            RestoreSourceState();
            ReleaseSlotTextures();
            if (_overlayMaterial != null)
            {
                UnityEngine.Object.Destroy(_overlayMaterial);
                _overlayMaterial = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            _sourceCamera = null;
            _backgroundCamera = null;
            _overlayCamera = null;
            _captureCamera = null;
            _overlayDrawer = null;
            _activeSlots.Clear();
        }

        private void EnsureCameras()
        {
            if (_root == null)
            {
                _root = new GameObject("__MainGameCharacterAfterimageRuntime");
                _root.hideFlags = HideFlags.DontSave;
            }

            if (_backgroundCamera == null)
            {
                GameObject go = new GameObject("AfterimageBackgroundCamera");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(_root.transform, false);
                _backgroundCamera = go.AddComponent<Camera>();
            }

            if (_overlayCamera == null)
            {
                GameObject go = new GameObject("AfterimageOverlayCamera");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(_root.transform, false);
                _overlayCamera = go.AddComponent<Camera>();
                _overlayDrawer = go.AddComponent<OverlayDrawer>();
                _overlayDrawer.Owner = this;
            }

            if (_captureCamera == null)
            {
                GameObject go = new GameObject("AfterimageCaptureCamera");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(_root.transform, false);
                _captureCamera = go.AddComponent<Camera>();
                _captureCamera.enabled = false;
            }
        }

        private void CaptureSourceState()
        {
            if (_sourceStateCaptured)
            {
                return;
            }
            if (_sourceCamera == null || !_sourceCamera)
            {
                return;
            }

            _sourceOriginalCullingMask = _sourceCamera.cullingMask;
            _sourceOriginalClearFlags = _sourceCamera.clearFlags;
            _sourceStateCaptured = true;
        }

        private void RestoreSourceState()
        {
            if (!_sourceStateCaptured)
            {
                return;
            }
            if (_sourceCamera == null || !_sourceCamera)
            {
                _sourceStateCaptured = false;
                return;
            }

            _sourceCamera.cullingMask = _sourceOriginalCullingMask;
            _sourceCamera.clearFlags = _sourceOriginalClearFlags;
            _sourceStateCaptured = false;
        }

        private void DisableAuxCameras()
        {
            if (_backgroundCamera != null)
            {
                _backgroundCamera.enabled = false;
            }
            if (_overlayCamera != null)
            {
                _overlayCamera.enabled = false;
            }
            if (_captureCamera != null)
            {
                _captureCamera.enabled = false;
                _captureCamera.targetTexture = null;
            }
        }

        private void EnsureOverlayMaterial()
        {
            string shaderName = (_settings != null && !string.IsNullOrEmpty(_settings.OverlayShaderName))
                ? _settings.OverlayShaderName
                : "Unlit/Transparent";

            if (_overlayMaterial != null && string.Equals(_overlayShaderName, shaderName, StringComparison.Ordinal))
                return;

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                if (_overlayMaterial != null)
                {
                    UnityEngine.Object.Destroy(_overlayMaterial);
                    _overlayMaterial = null;
                }
                _logWarn("overlay shader not found");
                return;
            }

            if (_overlayMaterial != null && _overlayMaterial.shader == shader)
            {
                return;
            }

            if (_overlayMaterial != null)
            {
                UnityEngine.Object.Destroy(_overlayMaterial);
            }

            _overlayMaterial = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };
            _overlayShaderName = shaderName;
        }

        private void EnsureSlotTextures()
        {
            if (_settings == null)
            {
                return;
            }

            int width = _settings.UseScreenSize || _settings.CaptureWidth <= 0 ? Screen.width : _settings.CaptureWidth;
            int height = _settings.UseScreenSize || _settings.CaptureHeight <= 0 ? Screen.height : _settings.CaptureHeight;
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            int slotCount = Mathf.Max(1, _settings.MaxAfterimageSlots);

            bool recreate = false;
            if (_slots == null || _slots.Length != slotCount)
            {
                recreate = true;
            }
            if (_captureWidth != width || _captureHeight != height)
            {
                recreate = true;
            }

            if (!recreate)
            {
                return;
            }

            ReleaseSlotTextures();

            _slots = new AfterimageSlot[slotCount];
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new AfterimageSlot
                {
                    Texture = CreateRenderTexture(width, height),
                    Life = 0,
                    Serial = 0
                };
            }

            _captureWidth = width;
            _captureHeight = height;
            _nextWriteSlot = 0;
            _captureSerial = 0;
            _activeSlotCount = 0;

            _logInfo("slot textures rebuilt: " + width + "x" + height + ", slots=" + slotCount);
        }

        private RenderTexture CreateRenderTexture(int width, int height)
        {
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            return rt;
        }

        private void ReleaseSlotTextures()
        {
            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                AfterimageSlot slot = _slots[i];
                if (slot == null)
                {
                    continue;
                }
                if (slot.Texture != null)
                {
                    slot.Texture.Release();
                    UnityEngine.Object.Destroy(slot.Texture);
                    slot.Texture = null;
                }
                slot.Life = 0;
                slot.Serial = 0;
            }
        }

        private void ClearSlots()
        {
            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Life = 0;
                _slots[i].Serial = 0;
                if (_slots[i].Texture != null)
                {
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = _slots[i].Texture;
                    GL.Clear(clearDepth: true, clearColor: true, Color.clear);
                    RenderTexture.active = previous;
                }
            }
            _activeSlotCount = 0;
        }

        private void SyncAuxCameras()
        {
            if (_sourceCamera == null || !_sourceCamera)
            {
                return;
            }

            float middleOffset = Mathf.Max(0.001f, _settings.MiddleCameraDepthOffsetMilli / 1000f);
            int sourceMask = _sourceOriginalCullingMask & _characterMask;
            int backgroundMask = _sourceOriginalCullingMask & ~_characterMask;
            if (sourceMask == 0)
            {
                DisableAuxCameras();
                RestoreSourceState();
                return;
            }

            SyncCopy(_backgroundCamera);
            _backgroundCamera.cullingMask = backgroundMask;
            _backgroundCamera.clearFlags = _sourceOriginalClearFlags;
            _backgroundCamera.depth = _sourceCamera.depth - (middleOffset * 2f);
            _backgroundCamera.enabled = true;

            SyncCopy(_overlayCamera);
            _overlayCamera.cullingMask = 0;
            _overlayCamera.clearFlags = CameraClearFlags.Depth;
            _overlayCamera.eventMask = 0;
            _overlayCamera.depth = _settings.OverlayInFrontOfCharacter
                ? (_sourceCamera.depth + middleOffset)
                : (_sourceCamera.depth - middleOffset);
            _overlayCamera.enabled = true;

            // Keep the original source camera as the final character pass.
            _sourceCamera.cullingMask = sourceMask;
            _sourceCamera.clearFlags = CameraClearFlags.Depth;

            SyncCopy(_captureCamera);
            _captureCamera.cullingMask = _characterMask;
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _captureCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _captureCamera.depth = _sourceCamera.depth;
            _captureCamera.enabled = false;
            _captureCamera.targetTexture = null;
        }

        private void SyncCopy(Camera dst)
        {
            if (dst == null || _sourceCamera == null)
            {
                return;
            }

            dst.CopyFrom(_sourceCamera);
            dst.transform.position = _sourceCamera.transform.position;
            dst.transform.rotation = _sourceCamera.transform.rotation;
            dst.transform.localScale = _sourceCamera.transform.localScale;
        }

        private void CaptureCharacterFrame()
        {
            if (_slots == null || _slots.Length == 0 || _captureCamera == null)
            {
                return;
            }

            AfterimageSlot slot = _slots[_nextWriteSlot];
            if (slot == null || slot.Texture == null)
            {
                return;
            }

            try
            {
                _captureCamera.targetTexture = slot.Texture;
                _captureCamera.Render();
                _captureCamera.targetTexture = null;
                slot.Life = Mathf.Max(1, _settings.FadeFrames);
                slot.Serial = ++_captureSerial;
                _nextWriteSlot = (_nextWriteSlot + 1) % _slots.Length;
            }
            catch (Exception ex)
            {
                _captureCamera.targetTexture = null;
                _logWarn("capture failed: " + ex.Message);
            }
        }

        private void AgeSlots()
        {
            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Life > 0)
                {
                    _slots[i].Life--;
                }
            }
        }

        private bool ResolveCharacterMask(bool force)
        {
            if (!force && _characterMask != 0)
            {
                return true;
            }

            if (!force && Time.unscaledTime < _nextLayerResolveTime)
            {
                return _characterMask != 0;
            }
            _nextLayerResolveTime = Time.unscaledTime + 1f;

            int mask = 0;
            string[] names = _settings?.CharacterLayerNames;
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    int layer = LayerMask.NameToLayer(name);
                    if (layer >= 0)
                    {
                        mask |= 1 << layer;
                    }
                }
            }

            int sourceMaskBase = _sourceStateCaptured ? _sourceOriginalCullingMask : (_sourceCamera != null ? _sourceCamera.cullingMask : 0);
            if (mask != 0 && sourceMaskBase != 0)
            {
                int intersected = mask & sourceMaskBase;
                if (intersected != 0)
                {
                    mask = intersected;
                }
            }

            _characterMask = mask;
            if (_characterMask == 0)
            {
                if (!_warnedNoCharacterLayer)
                {
                    _logWarn("character layer mask is empty. check CharacterLayerNames in settings json.");
                    _warnedNoCharacterLayer = true;
                }
                return false;
            }

            if (_warnedNoCharacterLayer)
            {
                _logInfo("character layer mask resolved: 0x" + _characterMask.ToString("X8"));
            }
            _warnedNoCharacterLayer = false;
            return true;
        }

        private static void SortSlotsByFadePriority(List<AfterimageSlot> slots)
        {
            if (slots == null || slots.Count < 2)
            {
                return;
            }

            for (int i = 1; i < slots.Count; i++)
            {
                AfterimageSlot key = slots[i];
                int j = i - 1;
                while (j >= 0)
                {
                    bool move = slots[j].Life < key.Life
                        || (slots[j].Life == key.Life && slots[j].Serial < key.Serial);
                    if (!move)
                    {
                        break;
                    }
                    slots[j + 1] = slots[j];
                    j--;
                }
                slots[j + 1] = key;
            }
        }
    }
}
