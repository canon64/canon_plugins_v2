using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private string _lastPoseKey = string.Empty;
        private string _lastPoseDisplayName = string.Empty;
        private bool _poseTrackingInitialized;
        private bool _poseTrackingPausedByDisable;
        private static readonly FieldInfo[] PoseKeyFields = GetPoseKeyFields();
        private static readonly PropertyInfo[] PoseKeyProperties = GetPoseKeyProperties();

        private static FieldInfo[] GetPoseKeyFields()
        {
            FieldInfo[] fields = typeof(HSceneProc.AnimationListInfo).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return fields;
        }

        private static PropertyInfo[] GetPoseKeyProperties()
        {
            PropertyInfo[] properties = typeof(HSceneProc.AnimationListInfo).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(properties, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return properties;
        }

        private bool TryGetCurrentPoseInfo(out string key, out string displayName)
        {
            key = string.Empty;
            displayName = string.Empty;
            try
            {
                HSceneProc proc = ResolveHSceneProcCached();
                if (proc == null || proc.flags == null || proc.flags.nowAnimationInfo == null)
                    return false;

                HSceneProc.AnimationListInfo info = proc.flags.nowAnimationInfo;
                string legacyKey = info.mode.ToString() + ":" + info.id.ToString();
                displayName = string.IsNullOrWhiteSpace(info.nameAnimation) ? legacyKey : info.nameAnimation;

                // mode:id だけだと、同じアニメID内の差分が全部同じ体位として扱われ、
                // 上書き保存が同じ PoseOverride を潰し続ける。
                // AnimationListInfo 内の安定した単純値も含めて、体位キーを細かくする。
                key = BuildCurrentPoseKey(info, legacyKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildCurrentPoseKey(object info, string fallbackKey)
        {
            if (info == null)
                return fallbackKey ?? string.Empty;

            StringBuilder sb = new StringBuilder(fallbackKey ?? string.Empty);
            FieldInfo[] fields = PoseKeyFields;
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || !IsStablePoseKeyType(field.FieldType))
                    continue;

                object value = null;
                try { value = field.GetValue(info); }
                catch { continue; }

                AppendPoseKeyPart(sb, field.Name, value);
            }

            PropertyInfo[] props = PoseKeyProperties;
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo prop = props[i];
                if (prop == null || prop.GetIndexParameters().Length != 0 || !prop.CanRead)
                    continue;
                if (!IsStablePoseKeyType(prop.PropertyType))
                    continue;

                object value = null;
                try { value = prop.GetValue(info, null); }
                catch { continue; }

                AppendPoseKeyPart(sb, prop.Name, value);
            }

            return sb.ToString();
        }

        private static bool IsStablePoseKeyType(Type type)
        {
            if (type == null)
                return false;
            if (type.IsEnum || type == typeof(string) || type == typeof(bool))
                return true;
            if (type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong))
                return true;
            return false;
        }

        private static void AppendPoseKeyPart(StringBuilder sb, string name, object value)
        {
            if (sb == null || string.IsNullOrWhiteSpace(name) || value == null)
                return;

            string raw = value.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return;

            sb.Append("|");
            sb.Append(SanitizePoseKeyToken(name));
            sb.Append("=");
            sb.Append(SanitizePoseKeyToken(raw));
        }

        private static string SanitizePoseKeyToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("|", "/").Replace("=", ":").Trim();
        }

        private void UpdatePosePresetTracking()
        {
            if (_settings == null)
                return;

            if (!_settings.PosePresetAutoApply)
            {
                HSceneProc proc = ResolveHSceneProcCached();
                _poseTrackingPausedByDisable = proc != null && proc.flags != null
                    && proc.flags.nowAnimationInfo != null;
                _poseTrackingInitialized = _poseTrackingPausedByDisable;
                return;
            }

            bool hasPose = TryGetCurrentPoseInfo(out string key, out string displayName);
            if (!hasPose)
            {
                _lastPoseKey = string.Empty;
                _lastPoseDisplayName = string.Empty;
                _poseTrackingInitialized = false;
                _poseTrackingPausedByDisable = false;
                return;
            }

            if (_poseTrackingPausedByDisable)
            {
                _poseTrackingPausedByDisable = false;
                _lastPoseKey = key;
                _lastPoseDisplayName = displayName;
                _poseTrackingInitialized = true;
                return;
            }

            if (!_poseTrackingInitialized)
            {
                bool probeReady = _subCamera != null && _cameraAnchorObject != null
                    && _displayObject != null && _displayAnchorObject != null;
                if (!probeReady)
                    return;

                _lastPoseKey = key;
                _lastPoseDisplayName = displayName;
                _poseTrackingInitialized = true;
                ApplyTaggedPoseOverrides(key);
                return;
            }

            if (string.Equals(key, _lastPoseKey, System.StringComparison.Ordinal))
                return;

            _lastPoseKey = key;
            _lastPoseDisplayName = displayName;
            ApplyTaggedPoseOverrides(key);
        }

        private void ApplyTaggedPoseOverrides(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!string.IsNullOrWhiteSpace(_activeCameraPresetName))
            {
                SubCameraPreset preset = FindPresetByName(_activeCameraPresetName);
                if (preset != null && preset.UsePoseOverrides && FindCameraPoseOverride(preset, key) != null)
                {
                    LoadPreset(preset, "pose-auto-apply");
                    LogInfo("camera pose override applied key=" + key + " preset=" + _activeCameraPresetName);
                }
            }

            if (_displayObject != null && _displayAnchorObject != null && !string.IsNullOrWhiteSpace(_activeDisplayPresetName))
            {
                DisplayPreset preset = FindDisplayPresetByName(_activeDisplayPresetName);
                if (preset != null && preset.UsePoseOverrides && FindDisplayPoseOverride(preset, key) != null)
                {
                    LoadDisplayPreset(preset);
                    LogInfo("display pose override applied key=" + key + " preset=" + _activeDisplayPresetName);
                }
            }
        }
    }
}
