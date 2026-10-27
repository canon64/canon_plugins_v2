using BepInEx.Configuration;
using UnityEngine;

namespace AstralMode
{
    /// <summary>
    /// アストラル界だけを照らす点光源。
    /// BlankMapAdd が blank 化のときにマップの Light を全て enabled=false にするため
    /// (SourceMapBlankifier.ConfigureLights / DisableLights=true)、アストラル界には光源が残らない。
    /// 丘の芝テクスチャも草も、光が無いので暗く潰れる。ここで界のぶんだけ光を足す。
    ///
    /// 現実を汚さないための決めごと:
    ///   - RenderSettings(ambient 等のグローバル設定)には一切触らない。触ると帰還時の復元漏れが現実に残る。
    ///     この GameObject を消せば必ず元通りになる、という一点だけで安全を担保する。
    ///   - 100m下の「止まった現実」を照らさない。Directional ではなく Point を使い、
    ///     さらに range に自動上限を掛けて構造的に届かないようにする。
    /// </summary>
    public sealed partial class Plugin
    {
        private GameObject _astralLightRoot;
        private Light _astralLight;

        // 下の現実までの距離に対して、ここまでしか光を伸ばさない。
        private const float RealWorldLightSafetyRatio = 0.8f;

        private void InitAstralLight()
        {
            AttachAstralLightConfigHandler(_cfgAstralLightIntensity);
            AttachAstralLightConfigHandler(_cfgAstralLightRange);
            AttachAstralLightConfigHandler(_cfgAstralLightHeight);
            if (_cfgAstralLightColor != null)
                _cfgAstralLightColor.SettingChanged += (sender, args) => ApplyAstralLightSettings();
            SimpleFileLogger.Info("Astral light init");
        }

        private void AttachAstralLightConfigHandler(ConfigEntry<float> entry)
        {
            if (entry == null) return;
            entry.SettingChanged += (sender, args) => ApplyAstralLightSettings();
        }

        internal void ShowAstralLight()
        {
            if (_cfgUseAstralLight == null || !_cfgUseAstralLight.Value)
            {
                HideAstralLight();
                SimpleFileLogger.Info("Astral light skipped by config");
                return;
            }

            if (_astralLightRoot == null)
            {
                _astralLightRoot = new GameObject("AstralMode_Light");
                Object.DontDestroyOnLoad(_astralLightRoot);
                _astralLightRoot.hideFlags = HideFlags.DontSave;

                _astralLight = _astralLightRoot.AddComponent<Light>();
                _astralLight.type = LightType.Point;
                // 影は落とさない。重いうえ、星空の下の淡い明かりに硬い影は合わない。
                _astralLight.shadows = LightShadows.None;
                SimpleFileLogger.Info("Astral light created");
            }

            ApplyAstralLightSettings();
            _astralLightRoot.SetActive(true);
            SimpleFileLogger.Info($"Astral light shown: pos={_astralLightRoot.transform.position}, "
                + $"intensity={_astralLight.intensity:F2}, range={_astralLight.range:F1}, color={_astralLight.color}");
        }

        internal void HideAstralLight()
        {
            if (_astralLightRoot != null)
            {
                _astralLightRoot.SetActive(false);
                SimpleFileLogger.Info("Astral light hidden for reuse");
            }
        }

        internal void DisposeAstralLight()
        {
            if (_astralLightRoot != null)
            {
                Object.Destroy(_astralLightRoot);
                _astralLightRoot = null;
                _astralLight = null;
                SimpleFileLogger.Info("Astral light disposed");
            }
        }

        private void ApplyAstralLightSettings()
        {
            if (_astralLightRoot == null || _astralLight == null) return;

            // 界の基準はアストラル側のキャラ中心。取れないときだけ従来の原点解決に落ちる。
            Vector3 basePos = GetAstralCharaCenter();
            if (basePos == Vector3.zero)
                basePos = GetAstralWorldOrigin();

            float height = _cfgAstralLightHeight != null ? _cfgAstralLightHeight.Value : 10f;
            Vector3 lightPos = basePos + Vector3.up * height;

            float requested = _cfgAstralLightRange != null ? _cfgAstralLightRange.Value : 80f;
            float range = ClampRangeAwayFromRealWorld(lightPos, requested);

            _astralLightRoot.transform.position = lightPos;
            _astralLight.intensity = _cfgAstralLightIntensity != null ? _cfgAstralLightIntensity.Value : 1.2f;
            _astralLight.range = range;
            _astralLight.color = _cfgAstralLightColor != null ? _cfgAstralLightColor.Value : new Color(0.72f, 0.80f, 1f, 1f);
        }

        /// <summary>
        /// 100m下の「止まった現実」に光が届かない範囲まで range を切り詰める。
        /// あそこはワープ瞬間を凍結した絵なので、勝手に明るくなると設計が破綻する。
        /// </summary>
        private float ClampRangeAwayFromRealWorld(Vector3 lightPos, float requested)
        {
            Vector3 realCenter = GetRealWorldCharaCenter();
            if (realCenter == Vector3.zero) return requested;

            float distance = Vector3.Distance(lightPos, realCenter);
            if (distance <= 0f) return requested;

            float limit = distance * RealWorldLightSafetyRatio;
            if (requested <= limit) return requested;

            SimpleFileLogger.Info($"Astral light range clamped: requested={requested:F1}, "
                + $"limit={limit:F1} (distance to frozen real world={distance:F1})");
            return limit;
        }
    }
}
