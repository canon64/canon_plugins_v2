using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGameGlow
{
    /// <summary>
    /// 外部プラグイン（変身演出など）から一時的にグローを焚くための経路。
    ///
    /// 通常運用の設定値（Strength/Tint/OverlayAlpha）は書き換えず、ここで持つ倍率を上から掛けるだけにする。
    /// 焚いている間だけ有効化されるので、常時OFF運用のまま「その瞬間だけ光る」使い方ができる。
    ///
    /// 除外レンダラは「男を光らせない」ための仕掛け。キャプチャは本編カメラの描画が終わった
    /// WaitForEndOfFrame 後に走るので、その一瞬だけレンダラを切っても画面には一切出ない。
    /// レイヤー移動を伴わないので、本編カメラやVRカメラのcullingMaskを壊す心配がない。
    /// </summary>
    public sealed partial class Plugin
    {
        private bool _externalGlowActive;
        internal bool ExternalGlowActive => _externalGlowActive;
        private float _externalGlowScale;
        private readonly Dictionary<string, float> _externalGlowDiagNextByStage = new Dictionary<string, float>();
        private int _externalGlowHiddenAppliedCount;
        private Renderer[] _externalGlowHidden = new Renderer[0];
        private bool[] _externalGlowHiddenPrev = new bool[0];
        private bool _externalGlowHideApplied;
        private bool _externalGlowDumped;
        private int _lastOverlayDrawFrame = -1;
        // 負なら「指定なし」＝MainGameGlow側の設定値をそのまま使う。
        private float _externalGlowThreshold = -1f;
        private float _externalGlowStrength = -1f;

        /// <summary>設定でOFFでも、外部が焚いているあいだだけは動かす。</summary>
        internal bool EffectiveEnabled
        {
            get { return (_cfgEnabled != null && _cfgEnabled.Value) || _externalGlowActive; }
        }

        /// <summary>
        /// 外部倍率は足す方向にだけ効かせる。常時グロー運用の最中に変身が起きても、
        /// 立ち上がりの0で既存のグローが一旦消える、という見え方にならないようにする。
        /// </summary>
        internal float EffectiveOverlayScale
        {
            get
            {
                if (!_externalGlowActive)
                    return 1f;

                float baseline = (_cfgEnabled != null && _cfgEnabled.Value) ? 1f : 0f;
                return Mathf.Max(baseline, Mathf.Clamp01(_externalGlowScale));
            }
        }

        /// <summary>
        /// 焚いているあいだだけ、経路のどこで止まっているか分かる粒度で出す。毎フレームは出さない。
        /// スロットルは段階ごとに持つ。共有にすると先に呼ばれた段階が枠を食い、後段が一生出ない。
        /// </summary>
        [System.Diagnostics.Conditional("MAINGAMEGLOW_DIAGNOSTICS")]
        internal void LogExternalGlowDiag(string stage, string detail)
        {
            if (!_externalGlowActive)
                return;

            float next;
            if (_externalGlowDiagNextByStage.TryGetValue(stage, out next) && Time.unscaledTime < next)
                return;

            _externalGlowDiagNextByStage[stage] = Time.unscaledTime + 0.25f;
            LogPlugin("DIAG", "external glow " + stage + ": " + detail);
        }

        /// <summary>
        /// キャプチャRTの中身を1回だけ書き出して、平均輝度をログに残す。
        /// 「撮れていない」のか「撮れているのに画面に出ていない」のかを分けるための計測。
        /// </summary>
        [System.Diagnostics.Conditional("MAINGAMEGLOW_DIAGNOSTICS")]
        private void DumpCaptureRtOnce()
        {
            if (_externalGlowDumped || _captureRt == null || string.IsNullOrEmpty(_pluginLogPath))
                return;

            _externalGlowDumped = true;

            RenderTexture prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = _captureRt;
                tex = new Texture2D(_captureRt.width, _captureRt.height, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0f, 0f, _captureRt.width, _captureRt.height), 0, 0);

                Color32[] pixels = tex.GetPixels32();
                double sumLuma = 0.0;
                double sumAlpha = 0.0;
                int bright = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    double luma = (0.299 * p.r + 0.587 * p.g + 0.114 * p.b) / 255.0;
                    sumLuma += luma;
                    sumAlpha += p.a / 255.0;
                    if (luma > 0.02) bright++;
                }

                int count = Mathf.Max(1, pixels.Length);
                string dir = Path.GetDirectoryName(_pluginLogPath) ?? string.Empty;
                string path = Path.Combine(dir, "capture_dump.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());

                LogPlugin("DIAG", "capture dump: meanLuma=" + (sumLuma / count).ToString("0.0000")
                    + " meanAlpha=" + (sumAlpha / count).ToString("0.0000")
                    + " brightPixels=" + bright + "/" + count
                    + " file=" + path);
            }
            catch (Exception ex)
            {
                LogPlugin("WARN", "capture dump failed: " + ex.Message);
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (tex != null)
                    Destroy(tex);
            }
        }

        /// <summary>オーバーレイに使う最終色。OnPostRender版とOnGUI版で同じ値を使う。</summary>
        internal bool TryGetOverlayColor(out Color color)
        {
            color = Color.clear;
            if (_captureRt == null || !EffectiveEnabled)
                return false;

            float a = Mathf.Clamp01(_cfgTintA.Value) * Mathf.Clamp01(_cfgOverlayAlpha.Value) * EffectiveOverlayScale;
            if (a <= 0.0001f)
                return false;

            color = new Color(
                Mathf.Clamp01(_cfgTintR.Value),
                Mathf.Clamp01(_cfgTintG.Value),
                Mathf.Clamp01(_cfgTintB.Value),
                a);
            return true;
        }

        /// <summary>
        /// 焚いているあいだだけIMGUIでも重ねる。OnPostRenderで描いた絵は、後段のイメージエフェクトに
        /// 上書きされると画面に残らない。IMGUIの描画はその後なので、そこで消える経路を持たない。
        /// </summary>
        private void OnGUI()
        {
            if (!_externalGlowActive || Event.current == null || Event.current.type != EventType.Repaint)
                return;

            // OnPostRender側が今フレーム描けているなら、こちらは出さない（二重に明るくなる）。
            if (_lastOverlayDrawFrame == Time.frameCount)
                return;

            // VRではIMGUIはデスクトップのミラーにしか出ない。HMDと食い違うので出さない。
            if (IsVrActive())
                return;

            Color color;
            if (!TryGetOverlayColor(out color))
                return;

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _captureRt, ScaleMode.StretchToFill, true);
            GUI.color = prev;

            LogExternalGlowDiag("imgui draw", "a=" + color.a.ToString("0.000")
                + " screen=" + Screen.width + "x" + Screen.height);
        }

        /// <summary>
        /// 焚き始めに一度だけ、カメラの実態を全部出す。どのカメラを基準にしているのか、
        /// そのカメラがキャラレイヤーを描いているのかを、推測ではなく数字で確定させるため。
        /// </summary>
        [System.Diagnostics.Conditional("MAINGAMEGLOW_DIAGNOSTICS")]
        private void LogCameraInventory()
        {
            try
            {
                Camera resolved = ResolveCamera();
                LogPlugin("DIAG", "camera inventory: resolved="
                    + (resolved != null ? resolved.name : "null")
                    + " charaMask=" + _characterMask
                    + " resolvedMask=" + (resolved != null ? resolved.cullingMask : 0)
                    + " intersect=" + (resolved != null ? (resolved.cullingMask & _characterMask) : 0));

                Camera[] all = Camera.allCameras;
                if (all == null)
                    return;

                for (int i = 0; i < all.Length; i++)
                {
                    Camera c = all[i];
                    if (c == null)
                        continue;

                    LogPlugin("DIAG", "camera[" + i + "] name=" + c.name
                        + " depth=" + c.depth.ToString("0.0")
                        + " enabled=" + c.enabled
                        + " ortho=" + c.orthographic
                        + " mask=" + c.cullingMask
                        + " charaBit=" + ((c.cullingMask & _characterMask) != 0)
                        + " rt=" + (c.targetTexture != null)
                        + " pos=" + c.transform.position.ToString("0.00"));
                }
            }
            catch (Exception ex)
            {
                LogPlugin("WARN", "camera inventory failed: " + ex.Message);
            }
        }

        internal bool BeginExternalGlow(GameObject[] excludeRoots)
        {
            RestoreExcludedRenderers();
            CollectExcludedRenderers(excludeRoots);
            _externalGlowScale = 0f;
            _externalGlowDiagNextByStage.Clear();
            _externalGlowDumped = false;
            _externalGlowActive = true;
            LogCameraInventory();
            LogPlugin("INFO", "external glow begin: excluded renderers=" + _externalGlowHidden.Length);
            return true;
        }

        internal bool SetExternalGlowScale(float scale)
        {
            if (!_externalGlowActive)
                return false;

            _externalGlowScale = Mathf.Clamp01(scale);

            // 一番明るいはずの所で1枚だけ中身を確かめる。
            if (_externalGlowScale >= 0.9f)
                DumpCaptureRtOnce();

            return true;
        }

        /// <summary>焚いている間だけ使う閾値と強さ。負を渡すとMainGameGlow側の設定値に戻る。</summary>
        internal bool SetExternalGlowParameters(float threshold, float strength)
        {
            _externalGlowThreshold = threshold;
            _externalGlowStrength = strength;
            LogPlugin("INFO", "external glow params: threshold="
                + (threshold >= 0f ? threshold.ToString("0.00") : "config")
                + " strength=" + (strength >= 0f ? strength.ToString("0.00") : "config"));
            return true;
        }

        internal bool EndExternalGlow()
        {
            if (!_externalGlowActive)
                return false;

            _externalGlowActive = false;
            _externalGlowScale = 0f;
            _externalGlowThreshold = -1f;
            _externalGlowStrength = -1f;
            // Capture is no longer called when ordinary glow is disabled, so
            // turn off its global post-process volume here as well.
            if (_cfgEnabled == null || !_cfgEnabled.Value)
            {
                if (_capturePostProcessLayer != null) _capturePostProcessLayer.enabled = false;
                if (_capturePostProcessVolume != null) _capturePostProcessVolume.enabled = false;
            }
            RestoreExcludedRenderers();
            _externalGlowHidden = new Renderer[0];
            _externalGlowHiddenPrev = new bool[0];
            LogPlugin("INFO", "external glow end");
            return true;
        }

        private void CollectExcludedRenderers(GameObject[] roots)
        {
            if (roots == null || roots.Length == 0)
            {
                _externalGlowHidden = new Renderer[0];
                _externalGlowHiddenPrev = new bool[0];
                return;
            }

            var collected = new List<Renderer>();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                // 非アクティブ分も拾う。元の enabled は退避してから戻すので、
                // もともと切ってあったレンダラを勝手に点けてしまうことはない。
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers == null)
                    continue;

                for (int j = 0; j < renderers.Length; j++)
                {
                    Renderer renderer = renderers[j];
                    if (renderer != null && !collected.Contains(renderer))
                        collected.Add(renderer);
                }
            }

            _externalGlowHidden = collected.ToArray();
            _externalGlowHiddenPrev = new bool[_externalGlowHidden.Length];
        }

        private void HideExcludedRenderers()
        {
            _externalGlowHiddenAppliedCount = 0;
            if (_externalGlowHideApplied || _externalGlowHidden == null || _externalGlowHidden.Length == 0)
                return;

            for (int i = 0; i < _externalGlowHidden.Length; i++)
            {
                Renderer renderer = _externalGlowHidden[i];
                if (renderer == null)
                    continue;

                _externalGlowHiddenPrev[i] = renderer.enabled;
                renderer.enabled = false;
                _externalGlowHiddenAppliedCount++;
            }

            _externalGlowHideApplied = true;
        }

        private void RestoreExcludedRenderers()
        {
            if (!_externalGlowHideApplied || _externalGlowHidden == null)
                return;

            for (int i = 0; i < _externalGlowHidden.Length; i++)
            {
                Renderer renderer = _externalGlowHidden[i];
                if (renderer == null)
                    continue;

                renderer.enabled = _externalGlowHiddenPrev[i];
            }

            _externalGlowHideApplied = false;
        }
    }
}
