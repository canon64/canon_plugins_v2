using BepInEx.Configuration;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private ConfigEntry<float> _cfgAstralSkyboxExposure;
        private ConfigEntry<float> _cfgAstralSkyboxTintR;
        private ConfigEntry<float> _cfgAstralSkyboxTintG;
        private ConfigEntry<float> _cfgAstralSkyboxTintB;

        private void InitAstralSkyboxMaterialConfig()
        {
            const string section = "60. Astral Skybox";

            _cfgAstralSkyboxExposure = Config.Bind(
                section,
                "Exposure",
                1.0f,
                new ConfigDescription(
                    "Skybox/Panoramic の _Exposure。1.0が元の明るさ。2.0以上で明るくなる。",
                    new AcceptableValueRange<float>(0.0f, 8.0f)));

            _cfgAstralSkyboxTintR = Config.Bind(
                section,
                "Tint R",
                1.0f,
                new ConfigDescription("Skybox _Tint のR。通常は1.0。", new AcceptableValueRange<float>(0.0f, 4.0f)));
            _cfgAstralSkyboxTintG = Config.Bind(
                section,
                "Tint G",
                1.0f,
                new ConfigDescription("Skybox _Tint のG。通常は1.0。", new AcceptableValueRange<float>(0.0f, 4.0f)));
            _cfgAstralSkyboxTintB = Config.Bind(
                section,
                "Tint B",
                1.0f,
                new ConfigDescription("Skybox _Tint のB。通常は1.0。", new AcceptableValueRange<float>(0.0f, 4.0f)));

            SimpleFileLogger.Info($"Astral skybox material config: Exposure={_cfgAstralSkyboxExposure.Value:F3}, Tint=({_cfgAstralSkyboxTintR.Value:F3},{_cfgAstralSkyboxTintG.Value:F3},{_cfgAstralSkyboxTintB.Value:F3})");
        }

        private void ApplyAstralSkyboxMaterialSettings()
        {
            if (_astralSkyboxMaterial == null) return;

            float exposure = _cfgAstralSkyboxExposure != null ? _cfgAstralSkyboxExposure.Value : 1.0f;
            Color tint = new Color(
                _cfgAstralSkyboxTintR != null ? _cfgAstralSkyboxTintR.Value : 1.0f,
                _cfgAstralSkyboxTintG != null ? _cfgAstralSkyboxTintG.Value : 1.0f,
                _cfgAstralSkyboxTintB != null ? _cfgAstralSkyboxTintB.Value : 1.0f,
                1.0f);

            if (_astralSkyboxMaterial.HasProperty("_Exposure"))
            {
                _astralSkyboxMaterial.SetFloat("_Exposure", exposure);
                SimpleFileLogger.Info($"Astral skybox exposure applied: _Exposure={exposure:F3}");
            }
            else
            {
                SimpleFileLogger.Warn($"Astral skybox exposure skipped: material has no _Exposure property. material={DescribeMaterial(_astralSkyboxMaterial)}");
            }

            if (_astralSkyboxMaterial.HasProperty("_Tint"))
            {
                _astralSkyboxMaterial.SetColor("_Tint", tint);
                SimpleFileLogger.Info($"Astral skybox tint applied: _Tint={tint}");
            }
            else
            {
                SimpleFileLogger.Info($"Astral skybox tint skipped: material has no _Tint property. material={DescribeMaterial(_astralSkyboxMaterial)}");
            }
        }
    }
}
