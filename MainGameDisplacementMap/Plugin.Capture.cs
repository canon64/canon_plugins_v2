using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRGIN.Core;

namespace MainGameDisplacementMap
{
    public sealed partial class Plugin
    {
        private Camera _captureCam;
        private Camera[] _cameraScanBuffer = new Camera[4];
        private int _cameraScanFrame = -1;
        private int _cameraScanCount;
        private readonly List<Camera> _cameraCandidates = new List<Camera>();
        private GameObject _captureRoot;
        private RenderTexture _rt;
        private int _rtW, _rtH;
        private Coroutine _captureCoroutine;
        private Camera _captureSource;
        private SourceFrameCapture _sourceFrameCapture;
        private int _sourceFrameCapturedAt = -1;
        private bool _sourceFrameCopyFailed;

        private sealed class SourceFrameCapture : MonoBehaviour
        {
            internal Plugin Owner;
            internal Camera SourceCamera;

            private void OnRenderImage(RenderTexture source, RenderTexture destination)
            {
                // The source camera must still present its unmodified image.
                Graphics.Blit(source, destination);
                Owner?.CopySourceFrame(SourceCamera, source);
            }
        }

        private OverlayDrawer _overlayDrawer;
        private Camera _lastOverlayTargetCamera;

        private HSceneProc _hscene;

        private int GetCameraScanCount()
        {
            if (_cameraScanFrame == Time.frameCount) return _cameraScanCount;
            _cameraScanFrame = Time.frameCount;
            int required = Camera.allCamerasCount;
            if (_cameraScanBuffer.Length < required)
                Array.Resize(ref _cameraScanBuffer, required);
            _cameraScanCount = Camera.GetAllCameras(_cameraScanBuffer);
            return _cameraScanCount;
        }

        private List<Camera> GetSortedCameraCandidates()
        {
            if (_cameraScanFrame == Time.frameCount)
                return _cameraCandidates;

            int cameraCount = GetCameraScanCount();
            _cameraCandidates.Clear();
            float minW = Screen.width * 0.75f;
            float minH = Screen.height * 0.75f;
            for (int i = 0; i < cameraCount; i++)
            {
                Camera c = _cameraScanBuffer[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c == _captureCam
                    || c.targetTexture != null || c.pixelWidth < minW || c.pixelHeight < minH
                    || c.rect.width < 0.75f || c.rect.height < 0.75f) continue;
                if (c.name.IndexOf("MoveAxisCamera", StringComparison.OrdinalIgnoreCase) >= 0
                    || c.name.IndexOf("BackCamera", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                _cameraCandidates.Add(c);
            }
            _cameraCandidates.Sort((a, b) => b.depth.CompareTo(a.depth));
            return _cameraCandidates;
        }
        private float _nextHSceneScan;

        private IEnumerator CaptureEndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            try
            {
                while (true)
                {
                    yield return wait;
                    Camera src = _captureSource;
                    if (!_cfgEnabled.Value || _rt == null || _captureCam == null || src == null)
                        continue;

                    if (_sourceFrameCapturedAt == Time.frameCount)
                        continue;

                    _captureCam.CopyFrom(src);

                    bool wantFull = _externalFullScreen || (_cfgFullScreen != null && _cfgFullScreen.Value);
                    if (wantFull)
                    {
                        int uiLayer = LayerMask.NameToLayer(_cfgFullSceneExcludeLayer.Value ?? "UI");
                        int exclude = uiLayer >= 0 ? (1 << uiLayer) : 0;
                        _captureCam.cullingMask = src.cullingMask & ~exclude;
                        _captureCam.clearFlags = src.clearFlags;
                        _captureCam.backgroundColor = src.backgroundColor;
                    }
                    else
                    {
                        _captureCam.clearFlags = CameraClearFlags.SolidColor;
                        _captureCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                        _captureCam.cullingMask = LayerMask.GetMask(_cfgCharaLayer.Value ?? "Chara");
                    }

                    _captureCam.targetTexture = _rt;
                    try { _captureCam.Render(); }
                    finally { if (_captureCam != null) _captureCam.targetTexture = null; }

                    if (_fileLog.Enabled && Time.unscaledTime - _lastCaptureLogTime > 1f)
                    {
                        _fileLog.Write("INFO", $"Captured frame (full={wantFull})");
                        _lastCaptureLogTime = Time.unscaledTime;
                    }
                }
            }
            finally
            {
                _captureCoroutine = null;
            }
        }

        private void CopySourceFrame(Camera camera, RenderTexture source)
        {
            if (camera == null || camera != _captureSource || source == null || _rt == null
                || source.width != _rtW || source.height != _rtH) return;
            try
            {
                Graphics.Blit(source, _rt);
                _sourceFrameCapturedAt = Time.frameCount;
            }
            catch
            {
                // Stop trying this path; end-of-frame capture keeps the original fallback.
                _sourceFrameCopyFailed = true;
            }
        }

        private void SyncSourceFrameCapture(Camera source, Camera overlayTarget)
        {
            bool wantFull = _externalFullScreen || (_cfgFullScreen != null && _cfgFullScreen.Value);
            int uiLayer = LayerMask.NameToLayer(_cfgFullSceneExcludeLayer.Value ?? "UI");
            bool excludesUi = uiLayer < 0 || (source.cullingMask & (1 << uiLayer)) == 0;
            bool clearColor = source.clearFlags == CameraClearFlags.Skybox
                || source.clearFlags == CameraClearFlags.SolidColor;
            bool eligible = !_sourceFrameCopyFailed && _cfgReuseSourceFrame.Value
                && !VR.Active && wantFull && excludesUi && clearColor
                && overlayTarget != source
                && source.targetTexture == null && source.pixelWidth == _rtW && source.pixelHeight == _rtH;

            if (_sourceFrameCapture != null && (!eligible || _sourceFrameCapture.SourceCamera != source))
            {
                _sourceFrameCapture.Owner = null;
                Destroy(_sourceFrameCapture);
                _sourceFrameCapture = null;
                _sourceFrameCapturedAt = -1;
            }
            if (eligible && _sourceFrameCapture == null)
            {
                try
                {
                    _sourceFrameCapture = source.gameObject.AddComponent<SourceFrameCapture>();
                    _sourceFrameCapture.Owner = this;
                    _sourceFrameCapture.SourceCamera = source;
                }
                catch
                {
                    _sourceFrameCopyFailed = true;
                }
            }
        }

        private bool IsHSceneActive()
        {
            if (Time.unscaledTime >= _nextHSceneScan)
            {
                _nextHSceneScan = Time.unscaledTime + 0.5f;
                if (_hscene == null || !_hscene)
                    _hscene = FindObjectOfType<HSceneProc>();
            }
            return _hscene != null && _hscene && _hscene.isActiveAndEnabled &&
                   _hscene.gameObject.activeInHierarchy && _hscene.flags != null;
        }

        private Camera ResolveCamera()
        {
            if (VR.Active && VR.Camera != null && VR.Camera.SteamCam != null)
                return ((Component)VR.Camera.SteamCam).GetComponent<Camera>();

            string filter = _cfgCameraFilter.Value ?? "";
            bool hasFilter = filter.Length > 0;

            List<Camera> cands = GetSortedCameraCandidates();

            if (cands.Count == 0) return null;

            // 診断強制指定（一時テスト用）
            int forcedIdx = _cfgDiagnosticCameraIndex.Value;
            if (forcedIdx > 0 && forcedIdx <= cands.Count)
            {
                Camera forced = cands[forcedIdx - 1];
                if (_fileLog != null && _fileLog.Enabled && Time.unscaledTime - Plugin._lastSelectedCameraLogTime > 1f)
                {
                    _fileLog?.Write("INFO", $"Selected capture camera: {forced.name} depth={forced.depth} (forced index {forcedIdx})");
                    Plugin._lastSelectedCameraLogTime = Time.unscaledTime;
                }
                return forced;
            }

            Camera selected = null;

            if (hasFilter)
            {
                foreach (var c in cands)
                {
                    if (c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        selected = c;
                        break;
                    }
                }
            }
            else
            {
                // 通常運用：名前・状態を考慮して選ぶ
                // 優先: name == "Camera" かつ depth が 0 付近、特殊パスっぽくないもの
                foreach (var c in cands)
                {
                    bool isMainCameraName = c.name.Equals("Camera", StringComparison.OrdinalIgnoreCase);
                    bool depthNearZero = c.depth >= -0.1f && c.depth <= 0.6f; // 0 or 0.5 くらいまで許容
                    bool notSpecialPath = c.cullingMask != 32 && c.clearFlags != CameraClearFlags.Depth;
                    bool notOriginLocked = !(c.transform.position == Vector3.zero && c.nearClipPlane < 0);

                    if (isMainCameraName && depthNearZero && notSpecialPath && notOriginLocked)
                    {
                        selected = c;
                        break;
                    }
                }

                if (selected == null)
                {
                    // フォールバック：depth順で一番上（ただし特殊なものは避ける）
                    foreach (var c in cands)
                    {
                        if (c.cullingMask != 32 && c.nearClipPlane >= 0)
                        {
                            selected = c;
                            break;
                        }
                    }
                    if (selected == null)
                    {
                        selected = cands[0];
                    }
                }
            }

            // 選択ログ（1秒に1回） - capture専用
            if (selected != null && _fileLog != null && _fileLog.Enabled
                && Time.unscaledTime - Plugin._lastSelectedCameraLogTime > 1f)
            {
                _fileLog?.Write("INFO", $"Selected capture camera: {selected.name} depth={selected.depth}");
                Plugin._lastSelectedCameraLogTime = Time.unscaledTime;
            }

            return selected;
        }

        private Camera ResolveOverlayDrawCamera()
        {
            // キャプチャ元とは独立して「最終画面に波紋が届く描画先カメラ」を探す
            // 基本フィルタはResolveCameraと共通だが、選択基準は「最も遅く描画される（depth最大）」もの
            if (VR.Active && VR.Camera != null && VR.Camera.SteamCam != null)
                return ((Component)VR.Camera.SteamCam).GetComponent<Camera>();

            string filter = _cfgCameraFilter.Value ?? "";
            bool hasFilter = filter.Length > 0;

            List<Camera> cands = GetSortedCameraCandidates();

            if (cands.Count == 0) return null;

            // 描画先は「最後に描画されるカメラ」＝depthが最大のものを優先
            Camera selected = cands[0];

            if (hasFilter)
            {
                foreach (var c in cands)
                {
                    if (c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        selected = c;
                        break;
                    }
                }
            }

            // overlay draw camera 専用ログ（captureログとは完全に分離）
            if (selected != null && _fileLog != null && _fileLog.Enabled
                && Time.unscaledTime - Plugin._lastOverlayDrawCameraLogTime > 1f)
            {
                _fileLog?.Write("INFO", $"Selected overlay draw camera: {selected.name} depth={selected.depth}");
                Plugin._lastOverlayDrawCameraLogTime = Time.unscaledTime;
            }

            return selected;
        }

        private void SetupCaptureCamera()
        {
            _captureRoot = new GameObject("MainGameDisplacementMapCapture");
            _captureRoot.hideFlags = HideFlags.DontSave;
            _captureCam = _captureRoot.AddComponent<Camera>();
            _captureCam.enabled = false;
            _captureCam.allowHDR = true;
            if (_fileLog != null) _fileLog.Write("INFO", "capture camera created");
        }

        private void EnsureCaptureResources(Camera src)
        {
            int w = Mathf.Max(64, _cfgUseScreenSize.Value ? Screen.width : _cfgCapW.Value);
            int h = Mathf.Max(64, _cfgUseScreenSize.Value ? Screen.height : _cfgCapH.Value);

            if (_rt == null || _rtW != w || _rtH != h)
            {
                ReleaseRt();
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _rt.Create();
                _rtW = w; _rtH = h;
                if (_fileLog != null && _cfgVerboseLog.Value) _fileLog.Write("INFO", $"RT recreated {w}x{h}");
            }

            if (_captureRoot == null)
            {
                SetupCaptureCamera();
            }
        }

        private void SyncOverlayDrawer(Camera drawTarget)
        {
            if (!_cfgEnabled.Value || drawTarget == null)
            {
                if (_overlayDrawer != null) { Destroy(_overlayDrawer); _overlayDrawer = null; }
                _lastOverlayTargetCamera = null;
                return;
            }

            if (_lastOverlayTargetCamera != drawTarget)
            {
                if (_overlayDrawer != null) { Destroy(_overlayDrawer); _overlayDrawer = null; }
                _overlayDrawer = drawTarget.gameObject.AddComponent<OverlayDrawer>();
                _overlayDrawer.Owner = this;
                _overlayDrawer.DrawCamera = drawTarget;
                _lastOverlayTargetCamera = drawTarget;
                if (_fileLog != null) _fileLog.Write("INFO", $"OverlayDrawer attached to {drawTarget.name} (overlay draw target)");
            }
        }

        private void ReleaseRt()
        {
            if (_rt == null) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
            _rtW = _rtH = 0;
        }

        private void StopRuntime()
        {
            if (_sourceFrameCapture != null)
            {
                _sourceFrameCapture.Owner = null;
                Destroy(_sourceFrameCapture);
                _sourceFrameCapture = null;
            }
            _sourceFrameCapturedAt = -1;
            _sourceFrameCopyFailed = false;
            if (_captureCoroutine != null) StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
            _captureSource = null;
            if (_overlayDrawer != null) { Destroy(_overlayDrawer); _overlayDrawer = null; }
            _lastOverlayTargetCamera = null;

            if (_captureCam != null) _captureCam.targetTexture = null;
            if (_captureRoot != null) { Destroy(_captureRoot); _captureRoot = null; }
            _captureCam = null;

            ReleaseRt();
            ReleaseMeshAndMat();
            ReleaseDisplacementMap();
        }
    }
}
