using System.Collections.Generic;
using UnityEngine;

namespace MainGameVrAfterimageLab
{
    public sealed partial class Plugin
    {
        // 元カメラに貼り付けて、残像を画面へ重ねる。
        // OnPostRender と OnRenderImage の両方を持ち、Config で選ばれた方だけが描く。
        // VRが左右の目をどう描くか(各フックが左右別に呼ばれるか)を実機ログで確認できる。
        internal sealed class OverlayDrawer : MonoBehaviour
        {
            internal Plugin Owner;

            private void OnPostRender()
            {
                if (Owner == null) return;
                if (Owner._cfgCompositeHook == null || Owner._cfgCompositeHook.Value != CompositeHook.PostRender) return;
                Owner.DrawTrails(GetComponent<Camera>(), null);
            }

            private void OnRenderImage(RenderTexture src, RenderTexture dst)
            {
                if (Owner == null) { Graphics.Blit(src, dst); return; }
                Graphics.Blit(src, dst);
                if (Owner._cfgCompositeHook == null || Owner._cfgCompositeHook.Value != CompositeHook.RenderImage) return;
                Owner.DrawTrails(GetComponent<Camera>(), dst);
            }
        }

        // 診断ログは「初めて見た状態」だけ出す（毎フレームのスパムを避ける）。
        private readonly HashSet<string> _loggedDrawSig = new HashSet<string>();

        // target==null: 現在のRenderTarget(=OnPostRender時のカメラ出力)へ描く。
        // target!=null: そのRenderTexture(=OnRenderImageのdst)へ描く。
        internal void DrawTrails(Camera cam, RenderTexture target)
        {
            if (!IsEffectivelyEnabled() || _slotsL == null || _life == null) return;

            // 今描いているのが右目で、かつ左右別モードなら右バッファを使う。それ以外は左(共通)。
            bool right = cam != null
                && _cfgEyeMode.Value == EyeMode.StereoPerEye
                && cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right;
            RenderTexture[] slots = right ? _slotsR : _slotsL;

            int len = _slotsL.Length;
            int fade = Mathf.Max(1, _cfgFadeFrames.Value);
            float aScale = Mathf.Clamp01(_cfgAlphaScale.Value);
            float tr = Mathf.Clamp01(_cfgTintR.Value);
            float tg = Mathf.Clamp01(_cfgTintG.Value);
            float tb = Mathf.Clamp01(_cfgTintB.Value);
            float ta = Mathf.Clamp01(_cfgTintA.Value);
            bool hideNewest = _cfgHideNewest.Value;
            int newest = (_writeIndex - 1 + len) % len;

            float w = cam != null ? cam.pixelWidth : Screen.width;
            float h = cam != null ? cam.pixelHeight : Screen.height;

            RenderTexture prev = null;
            if (target != null)
            {
                prev = RenderTexture.active;
                RenderTexture.active = target;
                w = target.width;
                h = target.height;
            }
            Rect rect = new Rect(0f, 0f, w, h);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            try
            {
                for (int i = 0; i < len; i++)
                {
                    int slot = (newest - i + len) % len;
                    if (_life[slot] <= 0) continue;
                    if (hideNewest && slot == newest) continue;
                    RenderTexture tex = slots[slot];
                    if (tex == null) continue;
                    float t = (float)_life[slot] / fade;
                    float alpha = ta * aScale * t;
                    if (alpha <= 0.0001f) continue;
                    Graphics.DrawTexture(rect, tex, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0,
                        new Color(tr, tg, tb, alpha));
                }
            }
            finally
            {
                GL.PopMatrix();
                if (target != null) RenderTexture.active = prev;
            }

            // 「どのフック経由で・どの目に・どの設定で描いたか」が変わった時だけ記録する。
            if (SimpleFileLogger.Enabled)
            {
                string via = target != null ? "RenderImage" : "PostRender";
                string drawSig = _cfgCompositeHook.Value + "|" + via + "|"
                    + (cam != null ? cam.name : "null") + "|"
                    + (cam != null ? cam.stereoActiveEye.ToString() : "?") + "|" + right;
                if (_loggedDrawSig.Add(drawSig))
                {
                    SimpleFileLogger.Info("draw hook=" + _cfgCompositeHook.Value
                        + " via=" + via
                        + " cam=" + (cam != null ? cam.name : "null")
                        + " activeEye=" + (cam != null ? cam.stereoActiveEye.ToString() : "?")
                        + " right=" + right);
                }
            }
        }
    }
}
