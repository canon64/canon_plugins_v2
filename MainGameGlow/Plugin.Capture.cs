using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameGlow
{
    public sealed partial class Plugin
    {
        private void SetupCaptureCamera()
        {
            _cameraRoot = new GameObject("MainGameGlowCapture");
            _cameraRoot.hideFlags = HideFlags.DontSave;

            _captureCamera = _cameraRoot.AddComponent<Camera>();
            _captureCamera.enabled = false;
            _captureCamera.allowHDR = true;

            _captureGlowInitFailed = false;
            _captureGlowTemplateWarned = false;

            LogPlugin("INFO", "capture camera created");
        }

        private RenderTexture CreateRT(int w, int h)
        {
            RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();
            return rt;
        }

        private void ReleaseCaptureRt()
        {
            if (_captureRt != null)
            {
                _captureRt.Release();
                Destroy(_captureRt);
                _captureRt = null;
            }
        }

        private Camera ResolveCamera()
        {
            if (_resolvedCameraFrame == Time.frameCount && _resolvedCamera != null
                && _resolvedCamera.enabled && _resolvedCamera.gameObject.activeInHierarchy)
                return _resolvedCamera;

            Camera resolved = ResolveCameraUncached();
            _resolvedCamera = resolved;
            _resolvedCameraFrame = Time.frameCount;
            return resolved;
        }

        private int _resolvedCameraFrame = -1;
        private Camera _resolvedCamera;
        private Camera[] _cameraScanBuffer = new Camera[8];
        private readonly List<Camera> _cameraCandidates = new List<Camera>(8);

        private static int CompareCameraDepth(Camera a, Camera b)
        {
            return b.depth.CompareTo(a.depth);
        }

        private Camera ResolveCameraUncached()
        {
            string filter = _cfgCameraNameFilter.Value ?? "";
            bool hasFilter = filter.Length > 0;

            if (_cfgPreferCameraMain.Value && Camera.main != null && Camera.main.enabled)
            {
                if (!hasFilter || Camera.main.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return Camera.main;
            }

            int cameraCount = Camera.allCamerasCount;
            if (cameraCount == 0)
                return null;

            if (_cameraScanBuffer.Length < cameraCount)
                Array.Resize(ref _cameraScanBuffer, Math.Max(cameraCount, _cameraScanBuffer.Length * 2));
            cameraCount = Camera.GetAllCameras(_cameraScanBuffer);
            List<Camera> candidates = _cameraCandidates;
            candidates.Clear();
            for (int i = 0; i < cameraCount; i++)
            {
                Camera c = _cameraScanBuffer[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                    continue;
                if (c == _captureCamera)
                    continue;
                candidates.Add(c);
            }

            if (candidates.Count == 0)
                return null;

            candidates.Sort(CompareCameraDepth);

            if (hasFilter)
            {
                foreach (Camera c in candidates)
                {
                    if (c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        return c;
                }
            }

            // depth が一番高いカメラはUI用のことがあり、キャラレイヤーを描いていない。
            // それを基準にすると、キャプチャは成功しているのに中身が空のRTができあがる。
            // キャラを描いているカメラを優先して掴む。
            if (_characterMask != 0)
            {
                foreach (Camera c in candidates)
                {
                    if ((c.cullingMask & _characterMask) != 0)
                        return c;
                }

                LogGlowDecision("no camera renders the character layer; capture will be empty");
            }

            int idx = Mathf.Clamp(_cfgCameraFallbackIndex.Value, 0, candidates.Count - 1);
            return candidates[idx];
        }

        private bool IsGlowRequested()
        {
            return _cfgGlowStrength != null
                && _cfgGlowBlurPercent != null
                && _cfgGlowStrength.Value > 0.0001f
                && _cfgGlowBlurPercent.Value > 0.0001f;
        }

        private void CaptureGlow(Camera src)
        {
            if (_captureCamera == null || _captureRt == null || src == null)
                return;

            _captureCamera.CopyFrom(src);
            // CopyFrom はカメラ設定しか写さない。位置と向きを自分で合わせないと、原点に置き去りのまま
            // キャラのいない方向を撮り続けて真っ黒のRTになる。MainGameCharacterAfterimage の
            // LayeredAfterimagePipeline.SyncCopy と同じ揃え方にする。
            _captureCamera.transform.position = src.transform.position;
            _captureCamera.transform.rotation = src.transform.rotation;
            _captureCamera.transform.localScale = src.transform.localScale;
            _captureCamera.cullingMask = _characterMask;
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _captureCamera.allowHDR = true;
            _captureCamera.targetTexture = _captureRt;

            bool pipelineReady = false;
            if (IsGlowRequested())
                pipelineReady = EnsureCaptureGlowPipeline(src);

            ApplyCaptureGlowSettings(pipelineReady);

            // 本編カメラは描画済みなので、ここで外部指定のレンダラ（男など）を切っても画面には出ない。
            HideExcludedRenderers();
            try
            {
                _captureCamera.Render();
            }
            finally
            {
                RestoreExcludedRenderers();
            }

            LogExternalGlowDiag("capture", "src=" + src.name
                + " hidden=" + _externalGlowHiddenAppliedCount
                + " rt=" + _captureRt.width + "x" + _captureRt.height
                + " mask=" + _characterMask
                + " pipeline=" + pipelineReady);

            _captureCamera.targetTexture = null;
        }
    }
}
