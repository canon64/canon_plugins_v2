using UnityEngine;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        // 描画用 MonoBehaviour
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

                Owner?.DrawOnPostRender(cam);
            }
        }

        private void AgeThenBuildDrawList()
        {
            InitWarpSlotMetadata();
            float tintA = Mathf.Clamp01(_cfgTintA.Value);

            for (int i = 0; i < _slots.Length; i++)
                AgeSlot(_timedSlots, _life, i);

            _drawCount = 0;
            int newest = (_writeIndex - 1 + _slots.Length) % _slots.Length;
            for (int i = 0; i < _slots.Length; i++)
            {
                int slot = (newest - i + _slots.Length) % _slots.Length;
                if (_life[slot] <= 0) continue;

                int fadeFrames = GetSlotFadeFrames(slot);
                float alphaScale = GetSlotAlphaScale(slot);
                float t = _timedSlots.IsTimed(slot) ? _timedSlots.Remaining(slot, Time.unscaledTime) : (float)_life[slot] / fadeFrames;
                t = ApplyCurve(t, _cfgFadeCurve.Value);
                float alpha = tintA * alphaScale * t;
                if (alpha <= 0.0001f) continue;

                _drawSlots[_drawCount] = _slots[slot];
                _drawAlpha[_drawCount] = alpha;
                _drawCount++;
            }
        }

        private static float ApplyCurve(float t, string curve)
        {
            switch (curve)
            {
                case "EaseIn":  return t * t;
                case "EaseOut": return Mathf.Sqrt(t);
                case "Square":  return t * t * t;
                default:        return t;
            }
        }

        internal void DrawOnPostRender(Camera cam)
        {
            if (_kaleidoscopeCompositeFrame == Time.frameCount) return;
            float w = (cam != null) ? cam.pixelWidth : Screen.width;
            float h = (cam != null) ? cam.pixelHeight : Screen.height;
            DrawOverlay(w, h, 1f);
        }

        public static void DrawAfterComposite(Camera cam, RenderTexture destination, int width, int height,
            float opacity)
        {
            Plugin plugin = Instance;
            if (plugin == null || cam == null || cam != plugin._lastSourceCamera || opacity <= .0001f) return;
            plugin._kaleidoscopeCompositeFrame = Time.frameCount;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.SetRenderTarget(destination);
                plugin.DrawOverlay(width, height, Mathf.Clamp01(opacity));
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private void DrawOverlay(float w, float h, float opacity)
        {
            if (!IsEffectivelyEnabled() || _drawCount == 0) return;
            float r = Mathf.Clamp01(_cfgTintR.Value);
            float g = Mathf.Clamp01(_cfgTintG.Value);
            float b = Mathf.Clamp01(_cfgTintB.Value);
            Rect rect = new Rect(0, 0, w, h);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            try
            {
                for (int i = 0; i < _drawCount; i++)
                {
                    if (_drawSlots[i] == null) continue;
                    float alpha = _drawAlpha[i] * opacity;
                    if (alpha <= 0.0001f) continue;
                    Graphics.DrawTexture(rect, _drawSlots[i], new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0,
                        new Color(r, g, b, alpha));
                }
            }
            finally
            {
                GL.PopMatrix();
            }
        }
    }
}
