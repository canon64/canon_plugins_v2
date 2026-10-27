using System;
using UnityEngine;

namespace MainGameGlow
{
    /// <summary>
    /// 外部プラグインから MainGameGlow を利用するための公開API。
    /// リフレクション経由で型解決される前提（直接DLL参照不要）。
    /// </summary>
    public static class MainGameGlowApi
    {
        /// <summary>変身など、外部からの一時グローが動作中かを返す。</summary>
        public static bool IsExternalGlowActive => Plugin.Instance != null && Plugin.Instance.ExternalGlowActive;

        /// <summary>MainGameGlowが内部で保持しているグロー出力RTを取得。</summary>
        public static bool TryGetGlowRenderTexture(out RenderTexture rt, out string reason)
        {
            rt = null;
            reason = string.Empty;
            try
            {
                return Plugin.TryGetGlowRenderTextureInternalApi(out rt, out reason);
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }

        /// <summary>MainGameGlowの有効/無効を取得。</summary>
        public static bool TryGetEnabled(out bool enabled)
        {
            return Plugin.TryGetEnabledInternalApi(out enabled);
        }

        /// <summary>MainGameGlowの有効/無効を設定。</summary>
        public static bool TrySetEnabled(bool enabled)
        {
            return Plugin.TrySetEnabledInternalApi(enabled);
        }

        /// <summary>
        /// 外部からの一時グローを開始する。<paramref name="excludeRoots"/> に渡した
        /// GameObject 配下のレンダラは、グローのキャプチャ時だけ外される（例: 男キャラを光らせない）。
        /// 開始直後の強さは0。以降 <see cref="TrySetExternalGlowScale"/> で振る。
        /// </summary>
        public static bool TryBeginExternalGlow(GameObject[] excludeRoots)
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.BeginExternalGlow(excludeRoots);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 一時グローの閾値と強さ。焚いている間だけMainGameGlow側の設定値を上書きする。
        /// 負を渡した項目は設定値のまま使う。
        /// </summary>
        public static bool TrySetExternalGlowParameters(float threshold, float strength)
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.SetExternalGlowParameters(threshold, strength);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>一時グローの強さ(0..1)。設定値の上に掛かる倍率で、下げる方向には効かない。</summary>
        public static bool TrySetExternalGlowScale(float scale)
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.SetExternalGlowScale(scale);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>一時グローを終了し、除外していたレンダラを元に戻す。</summary>
        public static bool TryEndExternalGlow()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.EndExternalGlow();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>現在のグロー設定値（Threshold/Strength/BlurPercent/Tint/OverlayAlpha）を取得。</summary>
        public static bool TryGetGlowParameters(
            out float threshold, out float strength, out float blurPercent,
            out float tintR, out float tintG, out float tintB, out float tintA, out float overlayAlpha,
            out string reason)
        {
            threshold = 0f; strength = 0f; blurPercent = 0f;
            tintR = 1f; tintG = 1f; tintB = 1f; tintA = 1f; overlayAlpha = 1f;
            reason = string.Empty;
            try
            {
                return Plugin.TryGetGlowParametersInternalApi(
                    out threshold, out strength, out blurPercent,
                    out tintR, out tintG, out tintB, out tintA, out overlayAlpha, out reason);
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }
    }
}
