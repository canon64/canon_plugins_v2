using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VRGIN.Core;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        private RenderTexture _captureScratch;
        private bool _copyCaptureTextureSupported;
        private readonly List<Renderer> _kaleidoscopeHiddenRenderers = new List<Renderer>();
        private bool _kaleidoscopeThoughtMaskActive;
        private ChaControl _kaleidoscopeThoughtActor;
        private int _kaleidoscopeCompositeFrame = -1;

        public static void SetKaleidoscopeThoughtMask(bool active, ChaControl actor)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            plugin._kaleidoscopeThoughtMaskActive = active;
            plugin._kaleidoscopeThoughtActor = active ? actor : null;
        }

        private void SetupCaptureCamera()
        {
            _cameraRoot = new GameObject("SimpleAfterimageCapture");
            _cameraRoot.hideFlags = HideFlags.DontSave;
            _captureCamera = _cameraRoot.AddComponent<Camera>();
            _captureCamera.enabled = false;
            _captureCamera.allowHDR = true;
            _captureGlowInitFailed = false;
            _captureGlowTemplateWarned = false;
            LogPlugin("INFO", "capture camera created");
        }

        private RenderTexture CreateRT(int w, int h, int depthBits = 0)
        {
            var rt = new RenderTexture(w, h, depthBits, RenderTextureFormat.ARGB32)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();
            return rt;
        }

        private void ReleaseSlots()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null) { _slots[i].Release(); Destroy(_slots[i]); _slots[i] = null; }
            }
        }

        private void ReleaseCaptureCameraRuntime()
        {
            if (_captureCamera != null)
                _captureCamera.targetTexture = null;

            if (_captureScratch != null)
            {
                _captureScratch.Release();
                Destroy(_captureScratch);
                _captureScratch = null;
            }
            _copyCaptureTextureSupported = false;

            CleanupCaptureGlowPipeline();

            if (_cameraRoot != null)
            {
                Destroy(_cameraRoot);
                _cameraRoot = null;
            }

            _captureCamera = null;
        }

        private bool IsHSceneActive()
        {
            if (Time.unscaledTime >= _nextHSceneScanTime)
            {
                _nextHSceneScanTime = Time.unscaledTime + 0.5f;
                if (_hSceneProc == null || !_hSceneProc)
                    _hSceneProc = FindObjectOfType<HSceneProc>();
            }

            return _hSceneProc != null
                && _hSceneProc
                && _hSceneProc.isActiveAndEnabled
                && _hSceneProc.gameObject.activeInHierarchy
                && _hSceneProc.flags != null;
        }

        private void StopAfterimageRuntime()
        {
            StopCaptureLoop();
            _externalGlowPaused = false;
            bool hadRuntime = _afterimageRuntimeReady
                || _overlayDrawer != null
                || _lastSourceCamera != null
                || _cameraRoot != null
                || _slots != null
                || _capturePostProcessLayer != null
                || _capturePostProcessVolume != null
                || _capturePostProcessProfile != null
                || _captureGlowVolumeRoot != null;

            _drawCount = 0;
            _frameCounter = 0;
            _writeIndex = 0;
            ClearVrStereoSlots();

            if (!hadRuntime)
                return;

            if (_overlayDrawer != null)
            {
                Destroy(_overlayDrawer);
                _overlayDrawer = null;
            }
            _lastSourceCamera = null;

            ReleaseCaptureCameraRuntime();

            ReleaseSlots();
            ReleaseVrStereoSlots();
            _slots = null;
            _life = null;
            _slotFadeFrames = null;
            _slotAlphaScale = null;
            _slotExternal = null;
            _drawSlots = null;
            _drawAlpha = null;
            _rtWidth = 0;
            _rtHeight = 0;
            _afterimageRuntimeReady = false;
        }

        private Camera ResolveCamera()
        {
            if (VR.Active && VR.Camera != null && VR.Camera.SteamCam != null)
                return ((Component)VR.Camera.SteamCam).GetComponent<Camera>();

            string filter = _cfgCameraNameFilter.Value ?? "";
            bool hasFilter = filter.Length > 0;

            Camera main = _cfgPreferCameraMain.Value ? Camera.main : null;
            if (main != null && main.enabled)
            {
                if (!hasFilter || main.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return main;
            }

            Camera[] all = Camera.allCameras;
            if (all == null || all.Length == 0) return null;

            var candidates = new List<Camera>(all.Length);
            foreach (Camera c in all)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c == _captureCamera) continue;
                candidates.Add(c);
            }
            if (candidates.Count == 0) return null;
            candidates.Sort((a, b) => b.depth.CompareTo(a.depth));

            if (hasFilter)
            {
                foreach (Camera c in candidates)
                    if (c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        return c;
            }

            int idx = Mathf.Clamp(_cfgCameraFallbackIndex.Value, 0, candidates.Count - 1);
            return candidates[idx];
        }

        private void SyncOverlayDrawer(Camera srcCamera)
        {
            if (!IsEffectivelyEnabled() || srcCamera == null)
            {
                if (_overlayDrawer != null) { Destroy(_overlayDrawer); _overlayDrawer = null; }
                _lastSourceCamera = null;
                return;
            }

            bool useVr = ShouldUseVrStereoRenderImage(srcCamera);
            if (_lastSourceCamera != srcCamera || _overlayDrawer == null ||
                (_overlayDrawer is VrOverlayDrawer) != useVr)
            {
                if (_overlayDrawer != null)
                {
                    _overlayDrawer.enabled = false;
                    Destroy(_overlayDrawer);
                    _overlayDrawer = null;
                }
                _overlayDrawer = useVr
                    ? (OverlayDrawer)srcCamera.gameObject.AddComponent<VrOverlayDrawer>()
                    : srcCamera.gameObject.AddComponent<MonoOverlayDrawer>();
                _overlayDrawer.Owner = this;
                _lastSourceCamera = srcCamera;
            }
        }

        private float GetHSceneSpeedIntensity()
        {
            if (!IsHSceneActive())
                return -1f;

            if (_hSceneProc == null || _hSceneProc.flags == null) return -1f;
            float maxSpeed = _hSceneProc.flags.speedMaxBody > 0f ? _hSceneProc.flags.speedMaxBody : 1f;
            return Mathf.Clamp01(_hSceneProc.flags.speedCalc / maxSpeed);
        }

        private void ScheduleCapture(Camera src)
        {
            _pendingCaptureSource = src;
            if (_captureLoop == null)
                _captureLoop = StartCoroutine(CaptureLoop());
        }

        private void StopCaptureLoop()
        {
            _pendingCaptureSource = null;
            if (_captureLoop == null) return;
            StopCoroutine(_captureLoop);
            _captureLoop = null;
        }

        private IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return _captureEndOfFrameWait;
                Camera src = _pendingCaptureSource;
                _pendingCaptureSource = null;
                if (src != null)
                    CaptureFrame(src);
            }
        }

        private void CaptureFrame(Camera src)
        {
            if (PauseForExternalGlow()) return;
            if (!IsEffectivelyEnabled() || !IsHSceneActive() || _slots == null || _captureCamera == null || src == null) return;

            InitWarpSlotMetadata();
            bool warpSlot = ShouldCaptureWarpAfterimage();
            if (IsBaseAfterimageCapturePausedByWarp() && !warpSlot) return;

            _captureCamera.CopyFrom(src);
            bool pipelineReady = false;
            if (IsCaptureGlowRequested())
                pipelineReady = EnsureCaptureGlowPipeline(src);
            ApplyCaptureGlowSettings(pipelineReady);
            if (warpSlot && IsWarpFullSceneActive())
            {
                // Whole scene minus UI: keep the source skybox/background so the ghosted frame
                // includes the full background, then drop only the excluded (UI) layer.
                int uiLayer = LayerMask.NameToLayer(_cfgFullSceneExcludeLayer.Value ?? "UI");
                int exclude = uiLayer >= 0 ? (1 << uiLayer) : 0;
                _captureCamera.cullingMask = src.cullingMask & ~exclude;
                _captureCamera.clearFlags = src.clearFlags;
                _captureCamera.backgroundColor = src.backgroundColor;
            }
            else
            {
                _captureCamera.cullingMask = _characterMask;
                _captureCamera.clearFlags = CameraClearFlags.SolidColor;
                _captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
            if (_captureScratch == null || _captureScratch.width != _rtWidth || _captureScratch.height != _rtHeight)
            {
                if (_captureScratch != null)
                {
                    _captureScratch.Release();
                    Destroy(_captureScratch);
                }
                _captureScratch = CreateRT(_rtWidth, _rtHeight, 24);
                _copyCaptureTextureSupported =
                    (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
            }

            _captureCamera.targetTexture = _captureScratch;
            try
            {
                if (_kaleidoscopeThoughtMaskActive && _kaleidoscopeThoughtActor != null)
                {
                    ChaControl character = _kaleidoscopeThoughtActor;
                    if (character != null)
                    {
                        foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>(true))
                        {
                            if (renderer == null || !renderer.enabled) continue;
                            _kaleidoscopeHiddenRenderers.Add(renderer);
                            renderer.enabled = false;
                        }
                    }
                }
                _captureCamera.Render();
            }
            finally
            {
                foreach (Renderer renderer in _kaleidoscopeHiddenRenderers)
                    if (renderer != null) renderer.enabled = true;
                _kaleidoscopeHiddenRenderers.Clear();
                if (_captureCamera != null) _captureCamera.targetTexture = null;
            }
            if (_slots[_writeIndex] == null)
                _slots[_writeIndex] = CreateRT(_rtWidth, _rtHeight);
            if (_copyCaptureTextureSupported)
                Graphics.CopyTexture(_captureScratch, _slots[_writeIndex]);
            else
                Graphics.Blit(_captureScratch, _slots[_writeIndex]);

            if (warpSlot) MarkCurrentSlotAsWarp();
            else MarkCurrentSlotAsNormal();

            _writeIndex = (_writeIndex + 1) % _slots.Length;
        }
    }
}
