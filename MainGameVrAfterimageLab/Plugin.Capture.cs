using System.Collections;
using UnityEngine;

namespace MainGameVrAfterimageLab
{
    public sealed partial class Plugin
    {
        private void SetupCaptureCamera()
        {
            _cameraRoot = new GameObject(PluginName + "Capture") { hideFlags = HideFlags.DontSave };
            _captureCamera = _cameraRoot.AddComponent<Camera>();
            _captureCamera.enabled = false;                      // 手動Renderのみ
            _captureCamera.allowHDR = true;
            _captureCamera.stereoTargetEye = StereoTargetEyeMask.None; // 単眼で撮る（左右の行列を手で入れる）
            SimpleFileLogger.Info("capture camera created");
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

        private void ReleaseSlots()
        {
            ReleaseArray(_slotsL); _slotsL = null;
            ReleaseArray(_slotsR); _slotsR = null;
            _life = null;
        }

        private void ReleaseArray(RenderTexture[] arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null) { arr[i].Release(); Destroy(arr[i]); arr[i] = null; }
        }

        private IEnumerator CaptureEndOfFrame(Camera src)
        {
            yield return new WaitForEndOfFrame();
            if (!IsEffectivelyEnabled() || !IsActiveScene() || _slotsL == null || _captureCamera == null || src == null)
                yield break;

            _captureCamera.CopyFrom(src);
            _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _captureCamera.enabled = false;

            if (_cfgScope.Value == CaptureScope.FullScreen)
            {
                int uiLayer = LayerMask.NameToLayer(_cfgExcludeLayer.Value ?? "UI");
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

            bool stereo = _cfgEyeMode.Value == EyeMode.StereoPerEye && src.stereoEnabled;
            if (stereo)
            {
                // 左目の視点で撮る
                _captureCamera.worldToCameraMatrix = src.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                _captureCamera.projectionMatrix = src.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                _captureCamera.targetTexture = _slotsL[_writeIndex];
                _captureCamera.Render();

                // 右目の視点で撮る
                _captureCamera.worldToCameraMatrix = src.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                _captureCamera.projectionMatrix = src.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                _captureCamera.targetTexture = _slotsR[_writeIndex];
                _captureCamera.Render();

                _captureCamera.ResetWorldToCameraMatrix();
                _captureCamera.ResetProjectionMatrix();
            }
            else
            {
                // Mono：1視点で1枚。合成時は左右とも _slotsL を貼る。
                _captureCamera.targetTexture = _slotsL[_writeIndex];
                _captureCamera.Render();
            }
            _captureCamera.targetTexture = null;

            _life[_writeIndex] = Mathf.Max(1, _cfgFadeFrames.Value);
            _writeIndex = (_writeIndex + 1) % _slotsL.Length;

            // 状態が変わった時だけ記録する（同じ状態は二度と出さない）。
            if (SimpleFileLogger.Enabled)
            {
                string capSig = src.name + "|" + src.stereoEnabled + "|" + _cfgEyeMode.Value + "|" + stereo
                    + "|" + src.stereoActiveEye + "|" + _cfgScope.Value + "|" + _rtWidth + "x" + _rtHeight;
                if (_loggedCaptureSig.Add(capSig))
                {
                    SimpleFileLogger.Info("capture src=" + src.name
                        + " stereoEnabled=" + src.stereoEnabled
                        + " eyeMode=" + _cfgEyeMode.Value
                        + " stereoUsed=" + stereo
                        + " activeEye=" + src.stereoActiveEye
                        + " scope=" + _cfgScope.Value
                        + " rt=" + _rtWidth + "x" + _rtHeight);
                }
            }
        }
    }
}
