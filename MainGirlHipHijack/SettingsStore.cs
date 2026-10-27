using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MainGameTransformGizmo;
using UnityEngine;

namespace MainGirlHipHijack
{
    internal static class SettingsStore
    {
        internal const string FileName = "FullIkGizmoSettings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly TimeSpan BackupMinInterval = TimeSpan.FromSeconds(30);
        private static readonly Dictionary<string, DateTime> LastBackupUtcByPath =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal static BodyIkGizmoSettings LoadOrCreate(
            string pluginDir,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            string path = Path.Combine(pluginDir, FileName);
            try
            {
                if (!File.Exists(path))
                {
                    var created = Normalize(new BodyIkGizmoSettings());
                    ResetVolatileFields(created);
                    SaveInternal(path, created, false, logWarn);
                    logInfo?.Invoke("settings created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                BodyIkGizmoSettings parsed = JsonUtility.FromJson<BodyIkGizmoSettings>(json);
                if (parsed == null)
                {
                    logWarn?.Invoke("settings parse failed, using defaults");
                    parsed = new BodyIkGizmoSettings();
                }

                parsed = Normalize(parsed);
                ResetVolatileFields(parsed);
                SaveInternal(path, parsed, false, logWarn);
                return parsed;
            }
            catch (Exception ex)
            {
                logError?.Invoke("settings load failed: " + ex.Message);
                return Normalize(new BodyIkGizmoSettings());
            }
        }

        internal static void Save(string pluginDir, BodyIkGizmoSettings settings, Action<string> logWarn)
        {
            string path = Path.Combine(pluginDir, FileName);
            SaveInternal(path, Normalize(settings), false, logWarn);
        }

        private static void ResetVolatileFields(BodyIkGizmoSettings settings)
        {
            settings.Enabled = new bool[Plugin.BIK_TOTAL]; // 全OFF
            settings.Weights = new float[Plugin.BIK_TOTAL];
            for (int i = 0; i < settings.Weights.Length; i++)
                settings.Weights[i] = 1f;
            settings.GizmoVisible = new bool[Plugin.BIK_TOTAL]; // 全非表示
        }

        private static BodyIkGizmoSettings Normalize(BodyIkGizmoSettings settings)
        {
            if (settings == null)
                settings = new BodyIkGizmoSettings();

            settings.GizmoSizeMultiplier = Mathf.Clamp(
                settings.GizmoSizeMultiplier,
                TransformGizmo.MinSizeMultiplier,
                TransformGizmo.MaxSizeMultiplier);
            settings.BodyIkDiagnosticLogInterval = Mathf.Clamp(settings.BodyIkDiagnosticLogInterval, 0.05f, 2f);
            settings.AutoPoseSwitchAnimationLoops = Mathf.Clamp(settings.AutoPoseSwitchAnimationLoops, 1, 999);
            settings.PoseTransitionSeconds = Mathf.Clamp(settings.PoseTransitionSeconds, 0f, 5f);
            if (!Enum.IsDefined(typeof(PoseTransitionEasing), settings.PoseTransitionEasing))
                settings.PoseTransitionEasing = PoseTransitionEasing.SmoothStep;
            settings.FollowSnapDistance = Mathf.Clamp(settings.FollowSnapDistance, 0.02f, 0.6f);
            if (settings.HmdFollowSnapDistance <= 0f)
                settings.HmdFollowSnapDistance = 0.3f;
            settings.HmdFollowSnapDistance = Mathf.Clamp(settings.HmdFollowSnapDistance, 0.02f, 0.6f);
            if (settings.FollowDistanceFadeRange <= 0f)
                settings.FollowDistanceFadeRange = 0.3f;
            settings.FollowDistanceFadeRange = Mathf.Clamp(settings.FollowDistanceFadeRange, 0.01f, 5f);
            settings.VRGrabDistance = Mathf.Clamp(settings.VRGrabDistance, 0.02f, 0.6f);
            settings.VRMarkerSize = Mathf.Clamp(settings.VRMarkerSize, 0.01f, 0.1f);
            settings.SpeedMoveAddPerSecond   = Mathf.Clamp(settings.SpeedMoveAddPerSecond,   0.001f, 20f);
            settings.SpeedDecayPerSecond     = Mathf.Clamp(settings.SpeedDecayPerSecond,     0.001f, 20f);
            settings.SpeedMovementThreshold  = (float)Math.Round(
                Mathf.Clamp(settings.SpeedMovementThreshold, 0.0001f, 0.1f), 4, MidpointRounding.AwayFromZero);
            settings.SpeedIdleDelay          = Mathf.Clamp(settings.SpeedIdleDelay,          0f, 5f);
            settings.BodyCtrlChangeFactorX   = Mathf.Clamp(settings.BodyCtrlChangeFactorX,   0f, 20f);
            settings.BodyCtrlChangeFactorY   = Mathf.Clamp(settings.BodyCtrlChangeFactorY,   0f, 20f);
            settings.BodyCtrlChangeFactorZ   = Mathf.Clamp(settings.BodyCtrlChangeFactorZ,   0f, 20f);
            settings.BodyCtrlDampen          = Mathf.Clamp(settings.BodyCtrlDampen,          0f, 1f);
            settings.MaleHmdHeadRotationWeight = Mathf.Clamp01(settings.MaleHmdHeadRotationWeight);
            settings.MaleHmdPositionScale = Mathf.Clamp(settings.MaleHmdPositionScale, 0f, 5f);
            settings.MaleHmdLocalDeltaSmoothing = Mathf.Clamp(settings.MaleHmdLocalDeltaSmoothing, 0f, 1f);
            if (!Enum.IsDefined(typeof(MaleHeadBoneSelectionMode), settings.MaleHeadBoneSelection))
                settings.MaleHeadBoneSelection = MaleHeadBoneSelectionMode.Auto;
            settings.MaleHeadIkPositionWeight = Mathf.Clamp01(settings.MaleHeadIkPositionWeight);
            settings.MaleHeadIkNeckWeight = Mathf.Clamp01(settings.MaleHeadIkNeckWeight);
            settings.MaleHeadIkSolveIterations = Mathf.Clamp(settings.MaleHeadIkSolveIterations, 1, 8);
            settings.MaleHeadIkNearDistance = Mathf.Clamp(settings.MaleHeadIkNearDistance, 0.05f, 2f);
            settings.MaleHeadIkFarDistance = Mathf.Clamp(settings.MaleHeadIkFarDistance, 0.05f, 2f);
            if (settings.MaleHeadIkFarDistance < settings.MaleHeadIkNearDistance + 0.01f)
                settings.MaleHeadIkFarDistance = settings.MaleHeadIkNearDistance + 0.01f;
            settings.MaleHeadIkNearWaistWeight = Mathf.Clamp01(settings.MaleHeadIkNearWaistWeight);
            settings.MaleHeadIkNearSpine1Weight = Mathf.Clamp01(settings.MaleHeadIkNearSpine1Weight);
            settings.MaleHeadIkNearSpine2Weight = Mathf.Clamp01(settings.MaleHeadIkNearSpine2Weight);
            settings.MaleHeadIkNearSpine3Weight = Mathf.Clamp01(settings.MaleHeadIkNearSpine3Weight);
            settings.MaleHeadIkNearNeckWeight = Mathf.Clamp01(settings.MaleHeadIkNearNeckWeight);
            settings.MaleHeadIkFarWaistWeight = Mathf.Clamp01(settings.MaleHeadIkFarWaistWeight);
            settings.MaleHeadIkFarSpine1Weight = Mathf.Clamp01(settings.MaleHeadIkFarSpine1Weight);
            settings.MaleHeadIkFarSpine2Weight = Mathf.Clamp01(settings.MaleHeadIkFarSpine2Weight);
            settings.MaleHeadIkFarSpine3Weight = Mathf.Clamp01(settings.MaleHeadIkFarSpine3Weight);
            settings.MaleHeadIkFarNeckWeight = Mathf.Clamp01(settings.MaleHeadIkFarNeckWeight);
            settings.MaleHeadIkBodyPullPositionWeight = Mathf.Clamp01(settings.MaleHeadIkBodyPullPositionWeight);
            settings.MaleHeadIkBodyPullRotationWeight = Mathf.Clamp01(settings.MaleHeadIkBodyPullRotationWeight);
            settings.MaleHeadIkBodyPullMaxStep = Mathf.Clamp(settings.MaleHeadIkBodyPullMaxStep, 0f, 0.5f);
            settings.MaleHeadIkCompensateBodyOffsetWeight = Mathf.Clamp(settings.MaleHeadIkCompensateBodyOffsetWeight, 0f, 2f);
            settings.MaleHeadIkCompensateBodyOffsetMax = Mathf.Clamp(settings.MaleHeadIkCompensateBodyOffsetMax, 0f, 3f);
            settings.MaleHeadIkSpineBodyOffsetSpine1Weight = Mathf.Clamp01(settings.MaleHeadIkSpineBodyOffsetSpine1Weight);
            settings.MaleHeadIkSpineBodyOffsetSpine2Weight = Mathf.Clamp01(settings.MaleHeadIkSpineBodyOffsetSpine2Weight);
            settings.MaleHeadIkSpineBodyOffsetSpine3Weight = Mathf.Clamp01(settings.MaleHeadIkSpineBodyOffsetSpine3Weight);
            settings.MaleHeadIkSpineBodyOffsetMax = Mathf.Clamp(settings.MaleHeadIkSpineBodyOffsetMax, 0f, 2f);
            settings.MaleNeckShoulderFollowPositionWeight = Mathf.Clamp01(settings.MaleNeckShoulderFollowPositionWeight);
            settings.MaleNeckShoulderFollowRotationWeight = Mathf.Clamp01(settings.MaleNeckShoulderFollowRotationWeight);
            settings.MaleSpine1MidpointT = Mathf.Clamp01(settings.MaleSpine1MidpointT);
            settings.MaleSpine1MidpointWeight = Mathf.Clamp01(settings.MaleSpine1MidpointWeight);
            settings.MaleSpine1MidpointRotationWeight = Mathf.Clamp01(settings.MaleSpine1MidpointRotationWeight);
            settings.MaleHmdDiagnosticLogInterval = Mathf.Clamp(settings.MaleHmdDiagnosticLogInterval, 0.05f, 2f);
            if (settings.MaleIkEnabled == null || settings.MaleIkEnabled.Length != Plugin.MALE_IK_BONE_TOTAL)
                settings.MaleIkEnabled = new bool[] { false, true, true, true, true, true };
            if (settings.MaleIkWeights == null || settings.MaleIkWeights.Length != Plugin.MALE_IK_BONE_TOTAL)
                settings.MaleIkWeights = new float[] { 0f, 0.30f, 0.45f, 0.65f, 0.85f, 1f };
            for (int i = 0; i < settings.MaleIkWeights.Length; i++)
                settings.MaleIkWeights[i] = Mathf.Clamp01(settings.MaleIkWeights[i]);
            settings.MaleHandFollowSnapDistance = Mathf.Clamp(settings.MaleHandFollowSnapDistance, 0.02f, 0.8f);
            if (settings.MaleControlEnabled == null || settings.MaleControlEnabled.Length != Plugin.BIK_TOTAL)
                settings.MaleControlEnabled = new bool[Plugin.BIK_TOTAL];
            if (settings.MaleControlGizmoVisible == null || settings.MaleControlGizmoVisible.Length != Plugin.BIK_TOTAL)
            {
                settings.MaleControlGizmoVisible = new bool[Plugin.BIK_TOTAL];
                for (int i = 0; i < settings.MaleControlGizmoVisible.Length; i++)
                    settings.MaleControlGizmoVisible[i] = true;
            }
            if (settings.MaleControlWeights == null || settings.MaleControlWeights.Length != Plugin.BIK_TOTAL)
            {
                settings.MaleControlWeights = new float[Plugin.BIK_TOTAL];
                for (int i = 0; i < settings.MaleControlWeights.Length; i++)
                    settings.MaleControlWeights[i] = 1f;
            }
            for (int i = 0; i < settings.MaleControlWeights.Length; i++)
                settings.MaleControlWeights[i] = Mathf.Clamp01(settings.MaleControlWeights[i]);

            if (settings.Enabled == null || settings.Enabled.Length != Plugin.BIK_TOTAL)
                settings.Enabled = new bool[Plugin.BIK_TOTAL];

            if (settings.GizmoVisible == null || settings.GizmoVisible.Length != Plugin.BIK_TOTAL)
            {
                settings.GizmoVisible = new bool[Plugin.BIK_TOTAL];
                for (int i = 0; i < settings.GizmoVisible.Length; i++)
                    settings.GizmoVisible[i] = true;
            }

            if (settings.Weights == null || settings.Weights.Length != Plugin.BIK_TOTAL)
            {
                settings.Weights = new float[Plugin.BIK_TOTAL];
                for (int i = 0; i < settings.Weights.Length; i++)
                    settings.Weights[i] = 1f;
            }

            for (int i = 0; i < settings.Weights.Length; i++)
                settings.Weights[i] = Mathf.Clamp01(settings.Weights[i]);

            settings.FemaleLeftHandPoseIndex = Mathf.Max(0, settings.FemaleLeftHandPoseIndex);
            settings.FemaleRightHandPoseIndex = Mathf.Max(0, settings.FemaleRightHandPoseIndex);
            settings.FemaleLeftHandPoseWeight = Mathf.Clamp01(settings.FemaleLeftHandPoseWeight);
            settings.FemaleRightHandPoseWeight = Mathf.Clamp01(settings.FemaleRightHandPoseWeight);
            NormalizeFemaleHandFk(settings.FemaleLeftHandFk);
            NormalizeFemaleHandFk(settings.FemaleRightHandFk);

            if (settings.FollowComposerEnabled == null || settings.FollowComposerEnabled.Length != Plugin.BIK_TOTAL)
                settings.FollowComposerEnabled = new bool[Plugin.BIK_TOTAL];
            if (settings.FollowComposerObjectIds == null || settings.FollowComposerObjectIds.Length != Plugin.BIK_TOTAL)
                settings.FollowComposerObjectIds = new string[Plugin.BIK_TOTAL];

            return settings;
        }

        private static void NormalizeFemaleHandFk(FemaleHandFkSettings hand)
        {
            if (hand == null)
                return;

            if (hand.Layers == null || hand.Layers.Length != 3)
            {
                var fixedLayers = new FemaleHandFkLayerSettings[3];
                for (int i = 0; i < fixedLayers.Length; i++)
                {
                    fixedLayers[i] = hand.Layers != null && i < hand.Layers.Length && hand.Layers[i] != null
                        ? hand.Layers[i]
                        : new FemaleHandFkLayerSettings();
                }
                hand.Layers = fixedLayers;
            }

            for (int i = 0; i < hand.Layers.Length; i++)
            {
                if (hand.Layers[i] == null)
                    hand.Layers[i] = new FemaleHandFkLayerSettings();
                hand.Layers[i].Index = Mathf.Max(0, hand.Layers[i].Index);
                hand.Layers[i].Weight = Mathf.Clamp01(hand.Layers[i].Weight);
            }
        }

        private static void SaveInternal(string path, BodyIkGizmoSettings settings, bool backup, Action<string> logWarn)
        {
            string tempPath = path + ".tmp";
            try
            {
                BodyIkGizmoSettings serializable = PrepareForSerialization(settings ?? new BodyIkGizmoSettings());
                string json = JsonUtility.ToJson(serializable, true);
                json = NormalizeJsonNumericLiterals(json) + Environment.NewLine;

                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path, Encoding.UTF8);
                    if (string.Equals(existing, json, StringComparison.Ordinal))
                        return;
                }

                File.WriteAllText(tempPath, json, Utf8NoBom);

                if (backup && File.Exists(path) && ShouldCreateBackup(path))
                    CreateBackup(path, logWarn);

                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        private static string NormalizeJsonNumericLiterals(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            var sb = new StringBuilder(json.Length);
            bool inString = false;
            bool escaping = false;
            int i = 0;

            while (i < json.Length)
            {
                char ch = json[i];

                if (inString)
                {
                    sb.Append(ch);
                    if (escaping)
                    {
                        escaping = false;
                    }
                    else if (ch == '\\')
                    {
                        escaping = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    sb.Append(ch);
                    i++;
                    continue;
                }

                if (!IsNumberTokenStart(ch))
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }

                int start = i;
                int end = i;

                if (json[end] == '-')
                    end++;

                bool hasDigits = false;
                while (end < json.Length && char.IsDigit(json[end]))
                {
                    hasDigits = true;
                    end++;
                }

                bool hasFraction = false;
                if (end < json.Length && json[end] == '.')
                {
                    hasFraction = true;
                    end++;
                    while (end < json.Length && char.IsDigit(json[end]))
                    {
                        hasDigits = true;
                        end++;
                    }
                }

                bool hasExponent = false;
                if (end < json.Length && (json[end] == 'e' || json[end] == 'E'))
                {
                    hasExponent = true;
                    end++;
                    if (end < json.Length && (json[end] == '+' || json[end] == '-'))
                        end++;

                    int expStart = end;
                    while (end < json.Length && char.IsDigit(json[end]))
                        end++;

                    if (end == expStart)
                        hasExponent = false;
                }

                string token = json.Substring(start, end - start);
                if (hasDigits && (hasFraction || hasExponent) &&
                    double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                    !double.IsNaN(parsed) && !double.IsInfinity(parsed))
                {
                    double rounded = Math.Round(parsed, 4, MidpointRounding.AwayFromZero);
                    if (Math.Abs(rounded) < 0.00005d)
                        rounded = 0d;

                    sb.Append(rounded.ToString("0.####", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(token);
                }

                i = end;
            }

            return sb.ToString();
        }

        private static bool IsNumberTokenStart(char ch)
        {
            return ch == '-' || char.IsDigit(ch);
        }
        private static BodyIkGizmoSettings PrepareForSerialization(BodyIkGizmoSettings source)
        {
            if (source == null)
                source = new BodyIkGizmoSettings();

            BodyIkGizmoSettings clone = source;
            try
            {
                clone = JsonUtility.FromJson<BodyIkGizmoSettings>(JsonUtility.ToJson(source, false)) ?? source;
            }
            catch
            {
                clone = source;
            }

            RoundFloatFieldsRecursive(clone, new HashSet<object>(ReferenceEqualityComparer.Instance));
            return clone;
        }

        private static void RoundFloatFieldsRecursive(object target, HashSet<object> visited)
        {
            if (target == null)
                return;

            Type type = target.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
                return;

            if (!type.IsValueType && !visited.Add(target))
                return;

            if (type.IsArray)
            {
                RoundArray((Array)target, type.GetElementType(), visited);
                return;
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                Type fieldType = field.FieldType;
                object value = field.GetValue(target);
                if (value == null)
                    continue;

                if (fieldType == typeof(float))
                {
                    field.SetValue(target, RoundFloat4((float)value));
                    continue;
                }

                if (fieldType == typeof(double))
                {
                    field.SetValue(target, RoundDouble4((double)value));
                    continue;
                }

                if (fieldType.IsArray)
                {
                    RoundArray((Array)value, fieldType.GetElementType(), visited);
                    continue;
                }

                if (!fieldType.IsPrimitive && !fieldType.IsEnum && fieldType != typeof(string) && fieldType != typeof(decimal))
                {
                    RoundFloatFieldsRecursive(value, visited);
                }
            }
        }

        private static void RoundArray(Array array, Type elementType, HashSet<object> visited)
        {
            if (array == null || elementType == null)
                return;

            if (elementType == typeof(float))
            {
                for (int i = 0; i < array.Length; i++)
                    array.SetValue(RoundFloat4((float)array.GetValue(i)), i);
                return;
            }

            if (elementType == typeof(double))
            {
                for (int i = 0; i < array.Length; i++)
                    array.SetValue(RoundDouble4((double)array.GetValue(i)), i);
                return;
            }

            if (elementType.IsPrimitive || elementType.IsEnum || elementType == typeof(string) || elementType == typeof(decimal))
                return;

            for (int i = 0; i < array.Length; i++)
            {
                object item = array.GetValue(i);
                if (item != null)
                    RoundFloatFieldsRecursive(item, visited);
            }
        }

        private static float RoundFloat4(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return value;
            return (float)Math.Round(value, 4, MidpointRounding.AwayFromZero);
        }

        private static double RoundDouble4(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value;
            return Math.Round(value, 4, MidpointRounding.AwayFromZero);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
        private static bool ShouldCreateBackup(string path)
        {
            DateTime now = DateTime.UtcNow;
            DateTime last;
            if (LastBackupUtcByPath.TryGetValue(path, out last))
            {
                if (now - last < BackupMinInterval)
                    return false;
            }

            LastBackupUtcByPath[path] = now;
            return true;
        }

        private static void CreateBackup(string path, Action<string> logWarn)
        {
            try
            {
                string backupPath = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(path, backupPath, true);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("settings backup failed: " + ex.Message);
            }
        }
    }
}

