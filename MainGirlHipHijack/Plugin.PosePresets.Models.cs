using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private const string PoseMotionStrengthUnknown = "unknown";
        private const string PoseMotionStrengthWeak = "weak";
        private const string PoseMotionStrengthStrong = "strong";

        [Serializable]
        private sealed class PosePresetRuntime
        {
            public string id;
            public string name;
            public string createdAt;
            public string screenshotFile;
            public int postureId = int.MinValue;
            public int postureMode = int.MinValue;
            public string postureName;
            public string postureStrength = PoseMotionStrengthUnknown;
            public int animatorStateHash;
            public bool autoApply;
            public bool hasFemaleHeadAdditive;
            public Quaternion femaleHeadAdditiveOffset = Quaternion.identity;
            public bool hasFemaleHeadAngle;
            public float femaleHeadAngleX;
            public float femaleHeadAngleY;
            public float femaleHeadAngleZ;
            public bool hasFemaleHandPose;
            public bool femaleLeftHandPoseEnabled;
            public bool femaleRightHandPoseEnabled;
            public int femaleLeftHandPoseIndex;
            public int femaleRightHandPoseIndex;
            public float femaleLeftHandPoseWeight = 1f;
            public float femaleRightHandPoseWeight = 1f;
            public PosePresetEntryRuntime[] entries = new PosePresetEntryRuntime[BIK_TOTAL];
        }

        [Serializable]
        private sealed class PosePresetEntryRuntime
        {
            public bool enabled;
            public float weight = 1f;
            public bool hasProxyPose;
            public Vector3 proxyPosition = Vector3.zero;
            public Quaternion proxyRotation = Quaternion.identity;
            public bool hasFollowBone;
            public bool followIsHmd;
            public bool followIsMale;
            public string followBonePath;
            public Vector3 followPositionOffset = Vector3.zero;
            public Quaternion followRotationOffset = Quaternion.identity;
        }

        [DataContract]
        private sealed class PosePresetCollectionFile
        {
            [DataMember(Order = 0)] public string format = PosePresetFormat;
            [DataMember(Order = 1)] public PosePresetFile[] presets = new PosePresetFile[0];
        }

        [DataContract]
        private sealed class PosePresetFile
        {
            [DataMember(Order = 0)] public string id;
            [DataMember(Order = 1)] public string name;
            [DataMember(Order = 2)] public string createdAt;
            [DataMember(Order = 3)] public string screenshotFile;
            [DataMember(Order = 4)] public int postureId;
            [DataMember(Order = 5)] public int postureMode;
            [DataMember(Order = 6)] public string postureName;
            [DataMember(Order = 7)] public bool autoApply;
            [DataMember(Order = 9)] public string postureStrength;
            [DataMember(Order = 8)] public PosePresetEntryFile[] entries = new PosePresetEntryFile[BIK_TOTAL];
            [DataMember(Order = 20)] public bool hasFemaleHeadAdditive;
            [DataMember(Order = 21)] public float femaleHeadAdditiveX;
            [DataMember(Order = 22)] public float femaleHeadAdditiveY;
            [DataMember(Order = 23)] public float femaleHeadAdditiveZ;
            [DataMember(Order = 24)] public float femaleHeadAdditiveW = 1f;
            [DataMember(Order = 25)] public bool hasFemaleHeadAngle;
            [DataMember(Order = 26)] public float femaleHeadAngleX;
            [DataMember(Order = 27)] public float femaleHeadAngleY;
            [DataMember(Order = 28)] public float femaleHeadAngleZ;
            [DataMember(Order = 40)] public bool hasFemaleHandPose;
            [DataMember(Order = 41)] public bool femaleLeftHandPoseEnabled;
            [DataMember(Order = 42)] public bool femaleRightHandPoseEnabled;
            [DataMember(Order = 43)] public int femaleLeftHandPoseIndex;
            [DataMember(Order = 44)] public int femaleRightHandPoseIndex;
            [DataMember(Order = 45)] public float femaleLeftHandPoseWeight = 1f;
            [DataMember(Order = 46)] public float femaleRightHandPoseWeight = 1f;
            [DataMember(Order = 50)] public int animatorStateHash;
        }

        [DataContract]
        private sealed class PosePresetEntryFile
        {
            [DataMember(Order = 0)] public bool enabled;
            [DataMember(Order = 1)] public float weight = 1f;
            [DataMember(Order = 2)] public bool hasProxyPose;
            [DataMember(Order = 3)] public float proxyPosX;
            [DataMember(Order = 4)] public float proxyPosY;
            [DataMember(Order = 5)] public float proxyPosZ;
            [DataMember(Order = 6)] public float proxyRotX;
            [DataMember(Order = 7)] public float proxyRotY;
            [DataMember(Order = 8)] public float proxyRotZ;
            [DataMember(Order = 9)] public float proxyRotW = 1f;
            [DataMember(Order = 10)] public bool hasFollowBone;
            [DataMember(Order = 19)] public bool followIsMale;
            [DataMember(Order = 20)] public bool followIsHmd;
            [DataMember(Order = 11)] public string followBonePath;
            [DataMember(Order = 12)] public float followPosOffsetX;
            [DataMember(Order = 13)] public float followPosOffsetY;
            [DataMember(Order = 14)] public float followPosOffsetZ;
            [DataMember(Order = 15)] public float followRotOffsetX;
            [DataMember(Order = 16)] public float followRotOffsetY;
            [DataMember(Order = 17)] public float followRotOffsetZ;
            [DataMember(Order = 18)] public float followRotOffsetW = 1f;
        }

        private const string PosePresetFormat = "MainGirlBodyIkPosePresetStoreV1";
        private static readonly UTF8Encoding PresetUtf8NoBom = new UTF8Encoding(false);

        private void InitPosePresetStorage()
        {
            _posePresetRootDir = Path.Combine(_pluginDir, "pose_presets");
            _posePresetShotsDir = Path.Combine(_posePresetRootDir, "shots");
            _posePresetIndexPath = Path.Combine(_posePresetRootDir, "index.json");

            Directory.CreateDirectory(_posePresetRootDir);
            Directory.CreateDirectory(_posePresetShotsDir);
        }

        private void EnsurePosePresetsLoaded()
        {
            if (_posePresetsLoaded)
                return;

            ReloadPosePresetIndex();
            _posePresetsLoaded = true;
        }

        private void ReloadPosePresetIndex()
        {
            try
            {
                _posePresets.Clear();
                if (File.Exists(_posePresetIndexPath))
                {
                    string json = File.ReadAllText(_posePresetIndexPath, Encoding.UTF8);
                    List<PosePresetRuntime> loaded = DeserializePosePresetIndex(json);
                    if (loaded != null && loaded.Count > 0)
                        _posePresets.AddRange(loaded);
                }

                NormalizePosePresets();
                _posePresetThumbDirty = true;
                LogInfo("[PosePreset] loaded count=" + _posePresets.Count);
            }
            catch (Exception ex)
            {
                _posePresets.Clear();
                _posePresetThumbDirty = true;
                LogError("[PosePreset] index load failed: " + ex.Message);
            }
        }

        private void SavePosePresetIndex()
        {
            try
            {
                NormalizePosePresets();
                PosePresetCollectionFile data = BuildPosePresetFileData(_posePresets);
                string json = SerializePosePresetFileData(data);
                File.WriteAllText(_posePresetIndexPath, json, PresetUtf8NoBom);
            }
            catch (Exception ex)
            {
                LogError("[PosePreset] index save failed: " + ex.Message);
            }
        }

        private static string SerializePosePresetFileData(PosePresetCollectionFile fileData)
        {
            var serializer = new DataContractJsonSerializer(typeof(PosePresetCollectionFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, fileData ?? new PosePresetCollectionFile());
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static PosePresetCollectionFile DeserializePosePresetFileData(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new PosePresetCollectionFile();

            var serializer = new DataContractJsonSerializer(typeof(PosePresetCollectionFile));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                var parsed = serializer.ReadObject(ms) as PosePresetCollectionFile;
                return parsed ?? new PosePresetCollectionFile();
            }
        }

        private static PosePresetCollectionFile BuildPosePresetFileData(List<PosePresetRuntime> runtime)
        {
            var result = new PosePresetCollectionFile();
            if (runtime == null || runtime.Count == 0)
            {
                result.presets = new PosePresetFile[0];
                return result;
            }

            var list = new PosePresetFile[runtime.Count];
            for (int i = 0; i < runtime.Count; i++)
            {
                PosePresetRuntime src = runtime[i] ?? new PosePresetRuntime();
                var dst = new PosePresetFile
                {
                    id = src.id,
                    name = src.name,
                    createdAt = src.createdAt,
                    screenshotFile = src.screenshotFile,
                    postureId = src.postureId,
                    postureMode = src.postureMode,
                    postureName = src.postureName,
                    postureStrength = NormalizePoseMotionStrength(src.postureStrength),
                    animatorStateHash = src.animatorStateHash,
                    autoApply = src.autoApply,
                    hasFemaleHeadAdditive = src.hasFemaleHeadAdditive,
                    femaleHeadAdditiveX = src.femaleHeadAdditiveOffset.x,
                    femaleHeadAdditiveY = src.femaleHeadAdditiveOffset.y,
                    femaleHeadAdditiveZ = src.femaleHeadAdditiveOffset.z,
                    femaleHeadAdditiveW = src.femaleHeadAdditiveOffset.w,
                    hasFemaleHeadAngle = src.hasFemaleHeadAngle,
                    femaleHeadAngleX = src.femaleHeadAngleX,
                    femaleHeadAngleY = src.femaleHeadAngleY,
                    femaleHeadAngleZ = src.femaleHeadAngleZ,
                    hasFemaleHandPose = src.hasFemaleHandPose,
                    femaleLeftHandPoseEnabled = src.femaleLeftHandPoseEnabled,
                    femaleRightHandPoseEnabled = src.femaleRightHandPoseEnabled,
                    femaleLeftHandPoseIndex = src.femaleLeftHandPoseIndex,
                    femaleRightHandPoseIndex = src.femaleRightHandPoseIndex,
                    femaleLeftHandPoseWeight = src.femaleLeftHandPoseWeight,
                    femaleRightHandPoseWeight = src.femaleRightHandPoseWeight,
                    entries = new PosePresetEntryFile[BIK_TOTAL]
                };

                for (int j = 0; j < BIK_TOTAL; j++)
                {
                    PosePresetEntryRuntime entry = src.entries != null && j < src.entries.Length && src.entries[j] != null
                        ? src.entries[j]
                        : new PosePresetEntryRuntime();

                    dst.entries[j] = new PosePresetEntryFile
                    {
                        enabled = entry.enabled,
                        weight = entry.weight,
                        hasProxyPose = entry.hasProxyPose,
                        proxyPosX = entry.proxyPosition.x,
                        proxyPosY = entry.proxyPosition.y,
                        proxyPosZ = entry.proxyPosition.z,
                        proxyRotX = entry.proxyRotation.x,
                        proxyRotY = entry.proxyRotation.y,
                        proxyRotZ = entry.proxyRotation.z,
                        proxyRotW = entry.proxyRotation.w,
                        hasFollowBone = entry.hasFollowBone,
                        followIsHmd = entry.followIsHmd,
                        followIsMale = entry.followIsMale,
                        followBonePath = entry.followBonePath,
                        followPosOffsetX = entry.followPositionOffset.x,
                        followPosOffsetY = entry.followPositionOffset.y,
                        followPosOffsetZ = entry.followPositionOffset.z,
                        followRotOffsetX = entry.followRotationOffset.x,
                        followRotOffsetY = entry.followRotationOffset.y,
                        followRotOffsetZ = entry.followRotationOffset.z,
                        followRotOffsetW = entry.followRotationOffset.w
                    };
                }

                list[i] = dst;
            }

            result.presets = list;
            return result;
        }

        private static List<PosePresetRuntime> DeserializePosePresetIndex(string json)
        {
            var result = new List<PosePresetRuntime>();
            try
            {
                PosePresetCollectionFile data = DeserializePosePresetFileData(json);
                if (data == null || data.presets == null || data.presets.Length == 0)
                    return result;

                for (int i = 0; i < data.presets.Length; i++)
                {
                    PosePresetFile src = data.presets[i] ?? new PosePresetFile();
                    var preset = new PosePresetRuntime
                    {
                        id = src.id,
                        name = src.name,
                        createdAt = src.createdAt,
                        screenshotFile = src.screenshotFile,
                        postureId = src.postureId,
                        postureMode = src.postureMode,
                        postureName = src.postureName,
                        postureStrength = NormalizePoseMotionStrength(src.postureStrength),
                        animatorStateHash = src.animatorStateHash,
                        autoApply = src.autoApply,
                        hasFemaleHeadAdditive = src.hasFemaleHeadAdditive,
                        femaleHeadAdditiveOffset = new Quaternion(
                            src.femaleHeadAdditiveX,
                            src.femaleHeadAdditiveY,
                            src.femaleHeadAdditiveZ,
                            src.femaleHeadAdditiveW),
                        hasFemaleHeadAngle = src.hasFemaleHeadAngle,
                        femaleHeadAngleX = src.femaleHeadAngleX,
                        femaleHeadAngleY = src.femaleHeadAngleY,
                        femaleHeadAngleZ = src.femaleHeadAngleZ,
                        hasFemaleHandPose = src.hasFemaleHandPose,
                        femaleLeftHandPoseEnabled = src.femaleLeftHandPoseEnabled,
                        femaleRightHandPoseEnabled = src.femaleRightHandPoseEnabled,
                        femaleLeftHandPoseIndex = src.femaleLeftHandPoseIndex,
                        femaleRightHandPoseIndex = src.femaleRightHandPoseIndex,
                        femaleLeftHandPoseWeight = src.femaleLeftHandPoseWeight,
                        femaleRightHandPoseWeight = src.femaleRightHandPoseWeight,
                        entries = new PosePresetEntryRuntime[BIK_TOTAL]
                    };

                    for (int j = 0; j < BIK_TOTAL; j++)
                    {
                        PosePresetEntryFile srcEntry = src.entries != null && j < src.entries.Length && src.entries[j] != null
                            ? src.entries[j]
                            : new PosePresetEntryFile();

                        preset.entries[j] = new PosePresetEntryRuntime
                        {
                            enabled = srcEntry.enabled,
                            weight = srcEntry.weight,
                            hasProxyPose = srcEntry.hasProxyPose,
                            proxyPosition = new Vector3(srcEntry.proxyPosX, srcEntry.proxyPosY, srcEntry.proxyPosZ),
                            proxyRotation = new Quaternion(srcEntry.proxyRotX, srcEntry.proxyRotY, srcEntry.proxyRotZ, srcEntry.proxyRotW),
                            hasFollowBone = srcEntry.hasFollowBone,
                            followIsHmd = srcEntry.followIsHmd,
                            followIsMale = srcEntry.followIsMale,
                            followBonePath = srcEntry.followBonePath,
                            followPositionOffset = new Vector3(srcEntry.followPosOffsetX, srcEntry.followPosOffsetY, srcEntry.followPosOffsetZ),
                            followRotationOffset = new Quaternion(srcEntry.followRotOffsetX, srcEntry.followRotOffsetY, srcEntry.followRotOffsetZ, srcEntry.followRotOffsetW)
                        };
                    }

                    result.Add(preset);
                }
            }
            catch
            {
                return result;
            }

            return result;
        }

        private void NormalizePosePresets()
        {
            for (int i = _posePresets.Count - 1; i >= 0; i--)
            {
                PosePresetRuntime preset = _posePresets[i];
                if (preset == null)
                {
                    _posePresets.RemoveAt(i);
                    continue;
                }

                NormalizePosePreset(preset);
            }
        }

        private static void NormalizePosePreset(PosePresetRuntime preset)
        {
            if (preset == null)
                return;

            if (preset.entries == null || preset.entries.Length != BIK_TOTAL)
            {
                PosePresetEntryRuntime[] fixedEntries = new PosePresetEntryRuntime[BIK_TOTAL];
                for (int i = 0; i < BIK_TOTAL; i++)
                {
                    fixedEntries[i] = preset.entries != null && i < preset.entries.Length && preset.entries[i] != null
                        ? preset.entries[i]
                        : new PosePresetEntryRuntime();
                }
                preset.entries = fixedEntries;
            }
            else
            {
                for (int i = 0; i < preset.entries.Length; i++)
                {
                    if (preset.entries[i] == null)
                        preset.entries[i] = new PosePresetEntryRuntime();
                }
            }

            preset.postureStrength = NormalizePoseMotionStrength(preset.postureStrength);

            for (int i = 0; i < preset.entries.Length; i++)
            {
                preset.entries[i].weight = Mathf.Clamp01(preset.entries[i].weight);
                if (!CanUseBoneFollow(i) || !preset.entries[i].hasFollowBone)
                {
                    preset.entries[i].hasFollowBone = false;
                    preset.entries[i].followIsHmd = false;
                    preset.entries[i].followIsMale = false;
                    preset.entries[i].followBonePath = null;
                    preset.entries[i].followPositionOffset = Vector3.zero;
                    preset.entries[i].followRotationOffset = Quaternion.identity;
                }
                else if (preset.entries[i].followIsHmd)
                {
                    preset.entries[i].followIsMale = false;
                    preset.entries[i].followBonePath = FollowHmdTargetName;
                    preset.entries[i].followRotationOffset = NormalizeSafeQuaternion(preset.entries[i].followRotationOffset);
                }
                else if (preset.entries[i].followBonePath == null)
                {
                    preset.entries[i].hasFollowBone = false;
                    preset.entries[i].followIsHmd = false;
                    preset.entries[i].followIsMale = false;
                    preset.entries[i].followPositionOffset = Vector3.zero;
                    preset.entries[i].followRotationOffset = Quaternion.identity;
                }
                else
                {
                    preset.entries[i].followRotationOffset = NormalizeSafeQuaternion(preset.entries[i].followRotationOffset);
                }
            }

            if (!preset.hasFemaleHeadAdditive)
            {
                preset.femaleHeadAdditiveOffset = Quaternion.identity;
            }
            else
            {
                preset.femaleHeadAdditiveOffset = NormalizeSafeQuaternion(preset.femaleHeadAdditiveOffset);
            }

            if (!preset.hasFemaleHeadAngle)
            {
                preset.femaleHeadAngleX = 0f;
                preset.femaleHeadAngleY = 0f;
                preset.femaleHeadAngleZ = 0f;
            }
            else
            {
                preset.femaleHeadAngleX = Mathf.Clamp(preset.femaleHeadAngleX, -120f, 120f);
                preset.femaleHeadAngleY = Mathf.Clamp(preset.femaleHeadAngleY, -120f, 120f);
                preset.femaleHeadAngleZ = Mathf.Clamp(preset.femaleHeadAngleZ, -120f, 120f);
            }

            if (!preset.hasFemaleHandPose)
            {
                preset.femaleLeftHandPoseEnabled = false;
                preset.femaleRightHandPoseEnabled = false;
                preset.femaleLeftHandPoseIndex = 0;
                preset.femaleRightHandPoseIndex = 0;
                preset.femaleLeftHandPoseWeight = 1f;
                preset.femaleRightHandPoseWeight = 1f;
            }
            else
            {
                preset.femaleLeftHandPoseIndex = Mathf.Max(0, preset.femaleLeftHandPoseIndex);
                preset.femaleRightHandPoseIndex = Mathf.Max(0, preset.femaleRightHandPoseIndex);
                preset.femaleLeftHandPoseWeight = Mathf.Clamp01(preset.femaleLeftHandPoseWeight);
                preset.femaleRightHandPoseWeight = Mathf.Clamp01(preset.femaleRightHandPoseWeight);
            }

        }

        private static string NormalizePoseMotionStrength(string value)
        {
            if (string.IsNullOrEmpty(value))
                return PoseMotionStrengthUnknown;

            string v = value.Trim().ToLowerInvariant();
            if (v == PoseMotionStrengthStrong)
                return PoseMotionStrengthStrong;
            if (v == PoseMotionStrengthWeak)
                return PoseMotionStrengthWeak;
            if (v == PoseMotionStrengthUnknown)
                return PoseMotionStrengthUnknown;

            return PoseMotionStrengthUnknown;
        }

        private static Quaternion NormalizeSafeQuaternion(Quaternion q)
        {
            float magSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (float.IsNaN(magSq) || float.IsInfinity(magSq) || magSq < 0.000001f)
                return Quaternion.identity;

            float invMag = 1f / Mathf.Sqrt(magSq);
            return new Quaternion(q.x * invMag, q.y * invMag, q.z * invMag, q.w * invMag);
        }

        private string BuildNumberedPosePresetName(string requestedName)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "pose" : requestedName.Trim();
            int next = 1;

            for (int i = 0; i < _posePresets.Count; i++)
            {
                PosePresetRuntime preset = _posePresets[i];
                if (preset == null || string.IsNullOrEmpty(preset.name))
                    continue;

                int n;
                if (!TryParsePresetSequence(preset.name, baseName, out n))
                    continue;
                if (n >= next)
                    next = n + 1;
            }

            return baseName + "_" + next.ToString("000");
        }

        private static bool TryParsePresetSequence(string fullName, string baseName, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(baseName))
                return false;

            string prefix = baseName + "_";
            if (!fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string suffix = fullName.Substring(prefix.Length);
            if (suffix.Length <= 0)
                return false;

            for (int i = 0; i < suffix.Length; i++)
            {
                if (!char.IsDigit(suffix[i]))
                    return false;
            }

            return int.TryParse(suffix, out number);
        }

        private string GetPosePresetScreenshotPath(PosePresetRuntime preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.screenshotFile))
                return null;

            return Path.Combine(_posePresetShotsDir, preset.screenshotFile);
        }
    }
}
