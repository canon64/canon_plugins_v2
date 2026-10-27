using UnityEngine;

namespace MainGameGlow
{
    public sealed partial class Plugin
    {
        private abstract class OverlayDrawer : MonoBehaviour
        {
            internal Plugin Owner;
        }

        private sealed class VrOverlayDrawer : OverlayDrawer
        {
            private void OnRenderImage(RenderTexture src, RenderTexture dst)
            {
                Camera cam = GetComponent<Camera>();
                if (Owner != null && Owner.ShouldUseVrStereoRenderImage(cam))
                {
                    Owner.DrawVrStereoOnRenderImage(cam, src, dst);
                    return;
                }

                Graphics.Blit(src, dst);
            }
        }

        private sealed class MonoOverlayDrawer : OverlayDrawer
        {
            private void OnPostRender()
            {
                Camera cam = GetComponent<Camera>();
                if (Owner != null && Owner.ShouldUseVrStereoRenderImage(cam))
                    return;

                Owner?.DrawOverlay(cam);
            }
        }

        private void SyncOverlayDrawer(Camera srcCamera)
        {
            if (!EffectiveEnabled || srcCamera == null)
            {
                if (_overlayDrawer != null)
                {
                    Destroy(_overlayDrawer);
                    _overlayDrawer = null;
                }

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

        internal void DrawOverlay(Camera cam)
        {
            if (!EffectiveEnabled || _captureRt == null)
            {
                LogExternalGlowDiag("draw skipped", "enabled=" + EffectiveEnabled + " rt=" + (_captureRt != null));
                return;
            }

            float r = Mathf.Clamp01(_cfgTintR.Value);
            float g = Mathf.Clamp01(_cfgTintG.Value);
            float b = Mathf.Clamp01(_cfgTintB.Value);
            float a = Mathf.Clamp01(_cfgTintA.Value) * Mathf.Clamp01(_cfgOverlayAlpha.Value) * EffectiveOverlayScale;

            // IMGUI側と二重に乗せないための目印。
            _lastOverlayDrawFrame = Time.frameCount;

            LogExternalGlowDiag("draw", "cam=" + (cam != null ? cam.name : "null")
                + " a=" + a.ToString("0.000")
                + " scale=" + EffectiveOverlayScale.ToString("0.000")
                + " tintA=" + _cfgTintA.Value.ToString("0.00")
                + " overlayAlpha=" + _cfgOverlayAlpha.Value.ToString("0.00"));

            if (a <= 0.0001f)
                return;

            float w = cam != null ? cam.pixelWidth : Screen.width;
            float h = cam != null ? cam.pixelHeight : Screen.height;
            Rect rect = new Rect(0, 0, w, h);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            try
            {
                Graphics.DrawTexture(
                    rect,
                    _captureRt,
                    new Rect(0f, 0f, 1f, 1f),
                    0, 0, 0, 0,
                    new Color(r, g, b, a));
            }
            finally
            {
                GL.PopMatrix();
            }
        }
    }
}
