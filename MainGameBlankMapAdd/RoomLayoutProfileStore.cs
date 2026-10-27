using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameBlankMapAdd
{
    [DataContract]
    internal sealed class RoomLayoutProfile
    {
        [DataMember(Order = 0)]
        public float Scale = 1f;

        [DataMember(Order = 1)]
        public float OffsetX = 0f;

        [DataMember(Order = 2)]
        public float OffsetY = -1f;

        [DataMember(Order = 3)]
        public float OffsetZ = 0f;

        [DataMember(Order = 4)]
        public float RotationX = 0f;

        [DataMember(Order = 5)]
        public float RotationY = 0f;

        [DataMember(Order = 6)]
        public float RotationZ = 0f;

        [DataMember(Order = 7)]
        public bool HasAudioGain = false;

        [DataMember(Order = 8)]
        public float AudioGain = 1f;

        [DataMember(Order = 9)]
        public bool HasSpeedLimitBreak = false;

        [DataMember(Order = 10)]
        public bool SpeedForceVanilla = false;

        [DataMember(Order = 11)]
        public bool SpeedEnableVideoTimeSpeedCues = false;

        [DataMember(Order = 12)]
        public float SpeedAppliedBpmMax = 0f;

        [DataMember(Order = 13)]
        public bool HasBeatSync = false;

        [DataMember(Order = 14)]
        public bool BeatEnabled = true;

        [DataMember(Order = 15)]
        public int BeatBpm = 128;

        [DataMember(Order = 16)]
        public bool BeatAutoMotionSwitch = true;

        [DataMember(Order = 17)]
        public bool HasRoomLayout = true;

        [DataMember(Order = 18)]
        public bool BeatAutoThreshold = true;

        [DataMember(Order = 19)]
        public float BeatLowThreshold = 0.3f;

        [DataMember(Order = 20)]
        public float BeatHighThreshold = 0.7f;

        [DataMember(Order = 21)]
        public float BeatLowIntensity = 0.25f;

        [DataMember(Order = 22)]
        public float BeatMidIntensity = 0.5f;

        [DataMember(Order = 23)]
        public float BeatHighIntensity = 1f;

        [DataMember(Order = 24)]
        public float BeatSmoothTime = 0.5f;

        [DataMember(Order = 25)]
        public float BeatStrongMotionBeats = 4f;

        [DataMember(Order = 26)]
        public float BeatWeakMotionBeats = 4f;

        [DataMember(Order = 27)]
        public float BeatLowPassHz = 150f;

        [DataMember(Order = 28)]
        public bool BeatVerboseLog = false;

        internal RoomLayoutProfile Clone()
        {
            return new RoomLayoutProfile
            {
                Scale = Scale,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                OffsetZ = OffsetZ,
                RotationX = RotationX,
                RotationY = RotationY,
                RotationZ = RotationZ,
                HasAudioGain = HasAudioGain,
                AudioGain = AudioGain,
                HasSpeedLimitBreak = HasSpeedLimitBreak,
                SpeedForceVanilla = SpeedForceVanilla,
                SpeedEnableVideoTimeSpeedCues = SpeedEnableVideoTimeSpeedCues,
                SpeedAppliedBpmMax = SpeedAppliedBpmMax,
                HasBeatSync = HasBeatSync,
                BeatEnabled = BeatEnabled,
                BeatBpm = BeatBpm,
                BeatAutoMotionSwitch = BeatAutoMotionSwitch,
                HasRoomLayout = HasRoomLayout,
                BeatAutoThreshold = BeatAutoThreshold,
                BeatLowThreshold = BeatLowThreshold,
                BeatHighThreshold = BeatHighThreshold,
                BeatLowIntensity = BeatLowIntensity,
                BeatMidIntensity = BeatMidIntensity,
                BeatHighIntensity = BeatHighIntensity,
                BeatSmoothTime = BeatSmoothTime,
                BeatStrongMotionBeats = BeatStrongMotionBeats,
                BeatWeakMotionBeats = BeatWeakMotionBeats,
                BeatLowPassHz = BeatLowPassHz,
                BeatVerboseLog = BeatVerboseLog
            };
        }

        internal void Normalize()
        {
            Scale = Clamp(Scale, 0.25f, 4f);
            OffsetX = Clamp(OffsetX, -20f, 20f);
            OffsetY = Clamp(OffsetY, -10f, 10f);
            OffsetZ = Clamp(OffsetZ, -20f, 20f);
            RotationX = NormalizeAngle(RotationX);
            RotationY = NormalizeAngle(RotationY);
            RotationZ = NormalizeAngle(RotationZ);
            if (HasAudioGain)
            {
                if (float.IsNaN(AudioGain) || float.IsInfinity(AudioGain) || AudioGain <= 0f)
                    AudioGain = 1f;
                AudioGain = Clamp(AudioGain, 0.1f, 6f);
            }
            else
            {
                AudioGain = 1f;
            }

            if (HasSpeedLimitBreak)
            {
                if (float.IsNaN(SpeedAppliedBpmMax) || float.IsInfinity(SpeedAppliedBpmMax) || SpeedAppliedBpmMax <= 0f)
                    SpeedAppliedBpmMax = 120f;
                SpeedAppliedBpmMax = Clamp(SpeedAppliedBpmMax, 1f, 999f);
            }
            else
            {
                SpeedAppliedBpmMax = 0f;
            }

            if (HasBeatSync)
            {
                BeatBpm = ClampInt(BeatBpm, 1, 999);
                if (float.IsNaN(BeatLowPassHz) || float.IsInfinity(BeatLowPassHz) || BeatLowPassHz <= 0f)
                    BeatLowPassHz = 150f;
                if (float.IsNaN(BeatLowThreshold) || float.IsInfinity(BeatLowThreshold))
                    BeatLowThreshold = 0.3f;
                if (float.IsNaN(BeatHighThreshold) || float.IsInfinity(BeatHighThreshold))
                    BeatHighThreshold = 0.7f;
                if (float.IsNaN(BeatLowIntensity) || float.IsInfinity(BeatLowIntensity))
                    BeatLowIntensity = 0.25f;
                if (float.IsNaN(BeatMidIntensity) || float.IsInfinity(BeatMidIntensity))
                    BeatMidIntensity = 0.5f;
                if (float.IsNaN(BeatHighIntensity) || float.IsInfinity(BeatHighIntensity))
                    BeatHighIntensity = 1f;
                if (float.IsNaN(BeatSmoothTime) || float.IsInfinity(BeatSmoothTime) || BeatSmoothTime < 0f)
                    BeatSmoothTime = 0.5f;
                if (float.IsNaN(BeatStrongMotionBeats) || float.IsInfinity(BeatStrongMotionBeats) || BeatStrongMotionBeats <= 0f)
                    BeatStrongMotionBeats = 4f;
                if (float.IsNaN(BeatWeakMotionBeats) || float.IsInfinity(BeatWeakMotionBeats) || BeatWeakMotionBeats <= 0f)
                    BeatWeakMotionBeats = 4f;

                BeatLowPassHz = Clamp(BeatLowPassHz, 50f, 500f);
                BeatLowThreshold = Clamp(BeatLowThreshold, 0f, 1f);
                BeatHighThreshold = Clamp(BeatHighThreshold, 0f, 1f);
                if (BeatHighThreshold <= BeatLowThreshold + 0.0001f)
                {
                    BeatLowThreshold = 0.3f;
                    BeatHighThreshold = 0.7f;
                }
                BeatLowIntensity = Clamp(BeatLowIntensity, 0f, 1f);
                BeatMidIntensity = Clamp(BeatMidIntensity, 0f, 1f);
                BeatHighIntensity = Clamp(BeatHighIntensity, 0f, 1f);
                if (BeatLowIntensity <= 0f && BeatMidIntensity <= 0f && BeatHighIntensity <= 0f)
                {
                    BeatLowIntensity = 0.25f;
                    BeatMidIntensity = 0.5f;
                    BeatHighIntensity = 1f;
                }
                BeatSmoothTime = Clamp(BeatSmoothTime, 0f, 2f);
                BeatStrongMotionBeats = Clamp(BeatStrongMotionBeats, 0.5f, 64f);
                BeatWeakMotionBeats = Clamp(BeatWeakMotionBeats, 0.5f, 64f);
            }
            else
            {
                BeatBpm = 128;
                BeatAutoThreshold = true;
                BeatLowThreshold = 0.3f;
                BeatHighThreshold = 0.7f;
                BeatLowIntensity = 0.25f;
                BeatMidIntensity = 0.5f;
                BeatHighIntensity = 1f;
                BeatSmoothTime = 0.5f;
                BeatStrongMotionBeats = 4f;
                BeatWeakMotionBeats = 4f;
                BeatLowPassHz = 150f;
                BeatVerboseLog = false;
            }
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float NormalizeAngle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;

            float normalized = value % 360f;
            if (normalized < 0f)
                normalized += 360f;
            return normalized;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    [DataContract]
    internal sealed class RoomLayoutProfileRecord
    {
        [DataMember(Order = 0)]
        public string Key = string.Empty;

        [DataMember(Order = 1)]
        public RoomLayoutProfile Profile = new RoomLayoutProfile();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Key == null)
                Key = string.Empty;
            if (Profile == null)
                Profile = new RoomLayoutProfile();
            Profile.Normalize();
        }
    }

    [DataContract]
    internal sealed class RoomLayoutProfilesDocument
    {
        [DataMember(Order = 0)]
        public List<RoomLayoutProfileRecord> FolderProfiles = new List<RoomLayoutProfileRecord>();

        [DataMember(Order = 1)]
        public List<RoomLayoutProfileRecord> VideoProfiles = new List<RoomLayoutProfileRecord>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (FolderProfiles == null)
                FolderProfiles = new List<RoomLayoutProfileRecord>();
            if (VideoProfiles == null)
                VideoProfiles = new List<RoomLayoutProfileRecord>();
        }
    }

    internal sealed class RoomLayoutProfileRepository
    {
        private readonly Dictionary<string, RoomLayoutProfile> _folderProfiles =
            new Dictionary<string, RoomLayoutProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RoomLayoutProfile> _videoProfiles =
            new Dictionary<string, RoomLayoutProfile>(StringComparer.OrdinalIgnoreCase);

        internal bool TryGetFolder(string key, out RoomLayoutProfile profile)
        {
            return TryGet(_folderProfiles, key, out profile);
        }

        internal bool TryGetVideo(string key, out RoomLayoutProfile profile)
        {
            return TryGet(_videoProfiles, key, out profile);
        }

        internal void SetFolder(string key, RoomLayoutProfile profile)
        {
            Set(_folderProfiles, key, profile);
        }

        internal void SetVideo(string key, RoomLayoutProfile profile)
        {
            Set(_videoProfiles, key, profile);
        }

        internal RoomLayoutProfilesDocument ToDocument()
        {
            return new RoomLayoutProfilesDocument
            {
                FolderProfiles = ToRecords(_folderProfiles),
                VideoProfiles = ToRecords(_videoProfiles)
            };
        }

        internal static RoomLayoutProfileRepository FromDocument(RoomLayoutProfilesDocument document)
        {
            var repository = new RoomLayoutProfileRepository();
            if (document == null)
                return repository;

            if (document.FolderProfiles != null)
            {
                for (int i = 0; i < document.FolderProfiles.Count; i++)
                {
                    RoomLayoutProfileRecord record = document.FolderProfiles[i];
                    if (record == null)
                        continue;
                    repository.SetFolder(record.Key, record.Profile);
                }
            }

            if (document.VideoProfiles != null)
            {
                for (int i = 0; i < document.VideoProfiles.Count; i++)
                {
                    RoomLayoutProfileRecord record = document.VideoProfiles[i];
                    if (record == null)
                        continue;
                    repository.SetVideo(record.Key, record.Profile);
                }
            }

            return repository;
        }

        private static bool TryGet(
            Dictionary<string, RoomLayoutProfile> source,
            string key,
            out RoomLayoutProfile profile)
        {
            profile = null;
            string normalized = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (!source.TryGetValue(normalized, out RoomLayoutProfile stored) || stored == null)
                return false;

            profile = stored.Clone();
            return true;
        }

        private static void Set(
            Dictionary<string, RoomLayoutProfile> source,
            string key,
            RoomLayoutProfile profile)
        {
            string normalized = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalized) || profile == null)
                return;

            RoomLayoutProfile cloned = profile.Clone();
            cloned.Normalize();
            source[normalized] = cloned;
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim();
        }

        private static List<RoomLayoutProfileRecord> ToRecords(
            Dictionary<string, RoomLayoutProfile> source)
        {
            var records = new List<RoomLayoutProfileRecord>(source.Count);
            var keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (!source.TryGetValue(key, out RoomLayoutProfile profile) || profile == null)
                    continue;

                records.Add(new RoomLayoutProfileRecord
                {
                    Key = key,
                    Profile = profile.Clone()
                });
            }

            return records;
        }
    }

    internal static class RoomLayoutProfileStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static RoomLayoutProfileRepository LoadOrCreate(
            string pluginDir,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            string path = GetPath(pluginDir);
            try
            {
                if (!File.Exists(path))
                {
                    var created = new RoomLayoutProfileRepository();
                    Save(path, created);
                    logInfo?.Invoke($"room layout profiles created: {path}");
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                RoomLayoutProfilesDocument parsed = Deserialize(json);
                if (parsed == null)
                {
                    logWarn?.Invoke("room layout profiles parse failed, fallback to empty");
                    var fallback = new RoomLayoutProfileRepository();
                    Save(path, fallback);
                    return fallback;
                }

                var loaded = RoomLayoutProfileRepository.FromDocument(parsed);
                Save(path, loaded);
                return loaded;
            }
            catch (Exception ex)
            {
                logError?.Invoke($"room layout profiles load failed: {ex.Message}");
                return new RoomLayoutProfileRepository();
            }
        }

        internal static void Save(string path, RoomLayoutProfileRepository repository)
        {
            var serializer = new DataContractJsonSerializer(typeof(RoomLayoutProfilesDocument));
            var document = repository?.ToDocument() ?? new RoomLayoutProfileRepository().ToDocument();
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, document);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        internal static string GetPath(string pluginDir)
        {
            return Path.Combine(pluginDir ?? string.Empty, "RoomLayoutProfiles.json");
        }

        private static RoomLayoutProfilesDocument Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(RoomLayoutProfilesDocument));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as RoomLayoutProfilesDocument;
            }
        }
    }
}
