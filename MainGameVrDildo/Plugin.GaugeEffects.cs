using System;
using System.Reflection;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private static readonly string[] GaugeEffectNames = { "SweatLevel", "TearLevel", "DroolLevel" };
        private static readonly string[] GaugeEffectCountNames = { "WetTexturesFaceCount", "TearTexturesCount", "DroolTexturesCount" };
        private readonly int[] _effectOriginal = new int[3];
        private readonly int[] _effectApplied = new int[3];
        private readonly bool[] _effectOwned = new bool[3];
        private ChaControl _effectFemale;
        private object _effectController;
        private Type _effectControllerType, _effectTextureType;
        private bool _oralFluidStarted;
        private float _oralContactSeconds;

        private void UpdateGaugeEffects()
        {
            bool ready = _settings != null && _female != null && _scene != null &&
                _scene.isActiveAndEnabled && _scene.flags != null;
            if (!ready) { ReleaseGaugeEffects(); return; }
            if (_effectFemale != _female)
            {
                ReleaseGaugeEffects();
                _effectFemale = _female;
            }
            bool oral = _settings.Enabled && _settings.OralFluidEnabled && _model != null &&
                _selectedTarget == TargetKind.Oral && _oralContact;
            if (oral)
            {
                _oralFluidStarted = true;
                _oralContactSeconds += Mathf.Max(0f, Time.deltaTime);
            }
            if (_effectController == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _effectControllerType = assembly.GetType("KK_SkinEffects.SkinEffectsController", false);
                    if (_effectControllerType == null) continue;
                    _effectTextureType = assembly.GetType("KK_SkinEffects.TextureLoader", false);
                    break;
                }
                if (_effectControllerType != null)
                    _effectController = _female.GetComponent(_effectControllerType);
            }
            if (_effectController == null) return;
            float gauge = _settings.MotionGaugeEnabled
                ? Mathf.Clamp01(_motionGaugeCm / _settings.MotionGaugeFullCm) : 0f;
            int sweat = 0;
            if (_model != null && _settings.Enabled && _settings.SweatEnabled &&
                _scene.flags.mode == HFlag.EMode.aibu)
            {
                if (gauge >= _settings.SweatStage1GaugeFraction) sweat = _settings.SweatStage1Level;
                if (gauge >= _settings.SweatStage2GaugeFraction) sweat = _settings.SweatStage2Level;
                if (gauge >= _settings.SweatStage3GaugeFraction) sweat = _settings.SweatStage3Level;
            }
            ApplyGaugeEffect(0, sweat);
            ApplyGaugeEffect(1, OralFluidLevel(_settings.OralTearLevel, _settings.OralTearStepSeconds));
            ApplyGaugeEffect(2, OralFluidLevel(_settings.OralDroolLevel, _settings.OralDroolStepSeconds));
        }

        private int OralFluidLevel(int maximum, float secondsPerLevel)
        {
            if (!_oralFluidStarted || !_settings.OralFluidEnabled || maximum <= 0) return 0;
            return Mathf.Min(maximum, 1 + Mathf.FloorToInt(_oralContactSeconds /
                Mathf.Max(0.5f, secondsPerLevel)));
        }

        private void ApplyGaugeEffect(int channel, int desired)
        {
            PropertyInfo property = _effectControllerType?.GetProperty(GaugeEffectNames[channel]);
            if (property == null || !property.CanWrite) return;
            try
            {
                int current = Convert.ToInt32(property.GetValue(_effectController, null));
                if (desired <= 0)
                {
                    RestoreGaugeEffect(channel, property, current);
                    return;
                }
                if (!_effectOwned[channel])
                {
                    _effectOriginal[channel] = current;
                    _effectOwned[channel] = true;
                }
                PropertyInfo count = _effectTextureType?.GetProperty(GaugeEffectCountNames[channel],
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                int available = count != null ? Convert.ToInt32(count.GetValue(null, null)) : 0;
                int value = Mathf.Max(_effectOriginal[channel],
                    Mathf.Clamp(desired, 0, available > 0 ? available : 3));
                if (current != value) property.SetValue(_effectController,
                    Convert.ChangeType(value, property.PropertyType), null);
                _effectApplied[channel] = value;
            }
            catch (Exception ex) { LogDiagnostic("Gauge effect " + GaugeEffectNames[channel] + ": " + ex.Message); }
        }

        private void RestoreGaugeEffect(int channel, PropertyInfo property, int current)
        {
            if (!_effectOwned[channel]) return;
            if (current == _effectApplied[channel] && current != _effectOriginal[channel])
                property.SetValue(_effectController,
                    Convert.ChangeType(_effectOriginal[channel], property.PropertyType), null);
            _effectOwned[channel] = false;
        }

        private void ReleaseGaugeEffects()
        {
            if (_effectController != null && _effectControllerType != null)
                for (int i = 0; i < GaugeEffectNames.Length; i++)
                {
                    PropertyInfo property = _effectControllerType.GetProperty(GaugeEffectNames[i]);
                    if (!_effectOwned[i] || property == null) continue;
                    try { RestoreGaugeEffect(i, property, Convert.ToInt32(property.GetValue(_effectController, null))); }
                    catch (Exception ex) { LogDiagnostic("Gauge effect restore: " + ex.Message); }
                }
            for (int i = 0; i < _effectOwned.Length; i++) _effectOwned[i] = false;
            _effectFemale = null;
            _effectController = null;
            _oralFluidStarted = false;
            _oralContactSeconds = 0f;
        }

        private void ReleaseGaugeSweat()
        {
            if (_effectController == null || _effectControllerType == null || !_effectOwned[0]) return;
            PropertyInfo property = _effectControllerType.GetProperty(GaugeEffectNames[0]);
            if (property == null) return;
            try { RestoreGaugeEffect(0, property, Convert.ToInt32(property.GetValue(_effectController, null))); }
            catch (Exception ex) { LogDiagnostic("Gauge sweat restore: " + ex.Message); }
        }
    }
}
