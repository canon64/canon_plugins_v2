using System;
using UnityEngine;
using VRGIN.Core;

namespace MainGameGlow
{
    /// <summary>
    /// VR時のグロー合成。
    ///
    /// VRでは本編カメラを CopyFrom した単眼カメラを1枚撮るやり方が通用しない。左右で視点が違うので、
    /// 片目基準の絵を両目に貼ると奥行きが合わず板に見える。SimpleAfterimage と同じく目ごとの
    /// OnRenderImage に割り込み、その目のビュー/射影行列でキャプチャして、その目の絵にだけ重ねる。
    ///
    /// IMGUI経路はVRではデスクトップのミラーにしか出ないので、VR時は使わない。
    /// </summary>
    public sealed partial class Plugin
    {
        private enum VrStereoEye
        {
            Mono,
            Left,
            Right
        }

        private RenderTexture _captureRtLeft;
        private RenderTexture _captureRtRight;
        private bool _vrStereoPathLogged;
        private CaptureMatrixKeeper _captureMatrixKeeper;

        /// <summary>
        /// 目ごとの射影行列を、実際の描画の直前にもう一度差し込み直す番人。
        ///
        /// PPv2 の PostProcessLayer.OnPreCull は、物理カメラでない限り必ず Camera.ResetProjectionMatrix() を呼ぶ。
        /// CaptureGlowForEye が Render() の直前に入れた目ごとの射影は、その Render() の中で捨てられ、
        /// fov/aspect から作った対称フラスタムに戻される。VRの目の射影は左右で反対方向に中心がずれた
        /// 非対称フラスタムなので、対称に戻された時点で光が横にずれる。view行列は触られないため、
        /// ずれるのは横方向だけになる。デスクトップは元から対称なので戻されても同じ絵になり症状が出ない。
        ///
        /// PPv2 が壊すのは OnPreCull だけで、PPv2 自身の OnPreRender は行列に触らない（コマンドバッファの
        /// 再構築のみ）。カリング後・描画前の OnPreRender で入れ直せば、描画には目の射影が使われる。
        /// カリングだけは対称フラスタムのまま走るが、写すのは画面内のキャラ一人なので実害はない。
        /// </summary>
        private sealed class CaptureMatrixKeeper : MonoBehaviour
        {
            internal bool Active;
            internal Matrix4x4 View;
            internal Matrix4x4 Projection;

            private void OnPreRender()
            {
                if (!Active)
                    return;

                Camera cam = GetComponent<Camera>();
                if (cam == null)
                    return;

                cam.worldToCameraMatrix = View;
                cam.projectionMatrix = Projection;
            }
        }

        /// <summary>VRGINが無い環境でも落ちないように、参照は必ず握り潰す。</summary>
        internal bool IsVrActive()
        {
            try
            {
                return VR.Active;
            }
            catch
            {
                return false;
            }
        }

        internal bool ShouldUseVrStereoRenderImage(Camera cam)
        {
            if (cam == null)
                return false;

            try
            {
                if (!VR.Active)
                    return false;

                Camera steamCam = ResolveSteamCamera();
                if (steamCam != null && ReferenceEquals(cam, steamCam))
                    return true;

                return cam.stereoEnabled || cam.stereoTargetEye != StereoTargetEyeMask.None;
            }
            catch
            {
                return false;
            }
        }

        private Camera ResolveSteamCamera()
        {
            if (VR.Camera == null || VR.Camera.SteamCam == null)
                return null;

            Component steamComponent = VR.Camera.SteamCam as Component;
            return steamComponent != null ? steamComponent.GetComponent<Camera>() : null;
        }

        private static VrStereoEye ResolveVrStereoEye(Camera cam)
        {
            if (cam == null)
                return VrStereoEye.Mono;

            if (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left)
                return VrStereoEye.Left;
            if (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right)
                return VrStereoEye.Right;

            if (cam.stereoTargetEye == StereoTargetEyeMask.Left)
                return VrStereoEye.Left;
            if (cam.stereoTargetEye == StereoTargetEyeMask.Right)
                return VrStereoEye.Right;

            string lower = (cam.name ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("left"))
                return VrStereoEye.Left;
            if (lower.Contains("right"))
                return VrStereoEye.Right;

            return VrStereoEye.Mono;
        }

        internal void DrawVrStereoOnRenderImage(Camera cam, RenderTexture src, RenderTexture dst)
        {
            if (src == null || dst == null)
                return;

            // 元の絵は何があっても必ず通す。ここで落とすと画面が消える。
            Graphics.Blit(src, dst);

            if (!EffectiveEnabled)
                return;

            Color color;
            if (!TryGetOverlayColor(out color))
                return;

            VrStereoEye eye = ResolveVrStereoEye(cam);
            RenderTexture eyeRt = EnsureEyeRt(eye, src.width, src.height);
            if (eyeRt == null)
                return;

            CaptureGlowForEye(cam, eye, eyeRt);
            DrawEyeGlow(dst, eyeRt, color);

            if (!_vrStereoPathLogged)
            {
                _vrStereoPathLogged = true;
                LogPlugin("INFO", "vr stereo render-image path active cam="
                    + cam.name + " eye=" + eye + " size=" + src.width + "x" + src.height);
            }
        }

        private RenderTexture EnsureEyeRt(VrStereoEye eye, int width, int height)
        {
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);

            bool right = eye == VrStereoEye.Right;
            RenderTexture current = right ? _captureRtRight : _captureRtLeft;

            if (current != null && current.width == width && current.height == height)
                return current;

            if (current != null)
            {
                current.Release();
                Destroy(current);
            }

            RenderTexture created = CreateRT(width, height);
            if (right)
                _captureRtRight = created;
            else
                _captureRtLeft = created;

            LogPlugin("INFO", "vr eye rt created: eye=" + eye + " size=" + width + "x" + height);
            return created;
        }

        /// <summary>
        /// その目のビュー/射影行列でキャラだけを撮る。行列を直接入れるので、
        /// 位置合わせは本編カメラと完全に一致する。
        /// </summary>
        private void CaptureGlowForEye(Camera src, VrStereoEye eye, RenderTexture target)
        {
            if (_captureCamera == null || src == null || target == null)
                return;

            _captureCamera.CopyFrom(src);
            _captureCamera.transform.position = src.transform.position;
            _captureCamera.transform.rotation = src.transform.rotation;
            _captureCamera.transform.localScale = src.transform.localScale;

            // 単眼カメラとして扱わないと、Render()が勝手に両目分を描こうとする。
            _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;

            if (eye != VrStereoEye.Mono)
            {
                Camera.StereoscopicEye stereoEye = eye == VrStereoEye.Right
                    ? Camera.StereoscopicEye.Right
                    : Camera.StereoscopicEye.Left;

                Matrix4x4 view = src.GetStereoViewMatrix(stereoEye);
                Matrix4x4 projection = src.GetStereoProjectionMatrix(stereoEye);

                _captureCamera.worldToCameraMatrix = view;
                _captureCamera.projectionMatrix = projection;

                // ここで入れた射影は Render() 中に PPv2 が捨てるので、番人に持たせて描画直前に入れ直す。
                ArmCaptureMatrixKeeper(view, projection);
            }
            else
            {
                DisarmCaptureMatrixKeeper();
            }

            _captureCamera.cullingMask = _characterMask;
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _captureCamera.allowHDR = true;
            _captureCamera.targetTexture = target;

            bool pipelineReady = false;
            if (IsGlowRequested() || (_externalGlowActive && _externalGlowStrength > 0.0001f))
                pipelineReady = EnsureCaptureGlowPipeline(src);

            ApplyCaptureGlowSettings(pipelineReady);

            HideExcludedRenderers();
            try
            {
                _captureCamera.Render();
            }
            finally
            {
                RestoreExcludedRenderers();
                DisarmCaptureMatrixKeeper();
            }

            _captureCamera.targetTexture = null;
            // 行列を差し込んだままにすると次のフレームの単眼撮影まで歪む。
            _captureCamera.ResetWorldToCameraMatrix();
            _captureCamera.ResetProjectionMatrix();

            LogExternalGlowDiag("vr capture", "eye=" + eye
                + " rt=" + target.width + "x" + target.height
                + " hidden=" + _externalGlowHiddenAppliedCount
                + " pipeline=" + pipelineReady);
        }

        /// <summary>
        /// 番人を張って、この目の行列を持たせる。キャプチャカメラは作り直されうるので、
        /// 生成時ではなくここで都度取り付けを確かめる。
        /// </summary>
        private void ArmCaptureMatrixKeeper(Matrix4x4 view, Matrix4x4 projection)
        {
            if (_captureCamera == null)
                return;

            if (_captureMatrixKeeper == null || _captureMatrixKeeper.gameObject != _captureCamera.gameObject)
                _captureMatrixKeeper = _captureCamera.gameObject.AddComponent<CaptureMatrixKeeper>();

            _captureMatrixKeeper.View = view;
            _captureMatrixKeeper.Projection = projection;
            _captureMatrixKeeper.Active = true;
        }

        /// <summary>単眼キャプチャ側まで行列を押し付けないよう、撮り終わったら必ず外す。</summary>
        private void DisarmCaptureMatrixKeeper()
        {
            if (_captureMatrixKeeper != null)
                _captureMatrixKeeper.Active = false;
        }

        /// <summary>撮った目の絵を、その目の最終画像へ重ねる。</summary>
        private void DrawEyeGlow(RenderTexture target, RenderTexture glow, Color color)
        {
            if (target == null || glow == null)
                return;

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
                Graphics.DrawTexture(
                    new Rect(0f, 0f, target.width, target.height),
                    glow,
                    new Rect(0f, 0f, 1f, 1f),
                    0, 0, 0, 0,
                    color);
            }
            catch (Exception ex)
            {
                LogPlugin("WARN", "vr eye glow draw failed: " + ex.Message);
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private void ReleaseEyeRts()
        {
            if (_captureRtLeft != null)
            {
                _captureRtLeft.Release();
                Destroy(_captureRtLeft);
                _captureRtLeft = null;
            }

            if (_captureRtRight != null)
            {
                _captureRtRight.Release();
                Destroy(_captureRtRight);
                _captureRtRight = null;
            }

            _vrStereoPathLogged = false;
        }
    }
}
