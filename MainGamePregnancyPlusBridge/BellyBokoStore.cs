using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGamePregnancyPlusBridge
{
    [DataContract]
    internal sealed class BellyBokoProfile
    {
        [DataMember(Order = 1)]
        public string AnimationKey = string.Empty;
        [DataMember(Order = 2)]
        public int PostureId = int.MinValue;
        [DataMember(Order = 3)]
        public int PostureMode = int.MinValue;
        [DataMember(Order = 4)]
        public string PostureName = string.Empty;
        [DataMember(Order = 5)]
        public string MotionStrength = "unknown";
        [DataMember(Order = 6)]
        public float DistanceMinMeters = 0.04f;
        [DataMember(Order = 7)]
        public float DistanceMaxMeters = 0.8f;
        [DataMember(Order = 8)]
        public float DistanceCutPercent = 0.9f;
        [DataMember(Order = 9)]
        public float DistanceSmoothing = 0.8f;
        [DataMember(Order = 10)]
        public string EaseUp = "easeOut";
        [DataMember(Order = 11)]
        public string EaseDown = "easeIn";
        [DataMember(Order = 12)]
        public float MinInflationSize = 0f;
        [DataMember(Order = 13)]
        public float MaxInflationSize = 5f;
    }

    [DataContract]
    internal sealed class BellyBokoProfileCollection
    {
        [DataMember(Order = 1)]
        public List<BellyBokoProfile> Profiles = new List<BellyBokoProfile>();
    }

    internal sealed class BellyBokoStore
    {
        private readonly string _path;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarn;
        private BellyBokoProfileCollection _collection;

        public BellyBokoStore(string path, Action<string> logInfo, Action<string> logWarn)
        {
            _path = path;
            _logInfo = logInfo;
            _logWarn = logWarn;
            _collection = LoadOrCreate();
        }

        public bool TryGet(string animationKey, out BellyBokoProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(animationKey) || _collection?.Profiles == null)
                return false;

            BellyBokoProfile found = _collection.Profiles.FirstOrDefault(x =>
                x != null && string.Equals(x.AnimationKey ?? string.Empty, animationKey, StringComparison.Ordinal));
            if (found == null)
                return false;

            profile = Clone(found);
            return true;
        }

        // postureId|postureMode|postureName|motionStrength|... のプレフィックス一致で検索
        public bool TryGetByMotionStrength(int postureId, int postureMode, string postureName, string motionStrength, out BellyBokoProfile profile)
        {
            profile = null;
            if (_collection?.Profiles == null)
                return false;

            string prefix = postureId + "|" + postureMode + "|" + (postureName ?? string.Empty) + "|" + (motionStrength ?? string.Empty) + "|";
            BellyBokoProfile found = _collection.Profiles.FirstOrDefault(x =>
                x != null && (x.AnimationKey ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal));
            if (found == null)
                return false;

            profile = Clone(found);
            return true;
        }

        public void Upsert(BellyBokoProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.AnimationKey))
                return;

            EnsureCollection();

            for (int i = _collection.Profiles.Count - 1; i >= 0; i--)
            {
                BellyBokoProfile cur = _collection.Profiles[i];
                if (cur != null && string.Equals(cur.AnimationKey ?? string.Empty, profile.AnimationKey, StringComparison.Ordinal))
                    _collection.Profiles.RemoveAt(i);
            }

            _collection.Profiles.Add(Clone(profile));
            _collection.Profiles = _collection.Profiles
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.AnimationKey))
                .OrderBy(x => x.AnimationKey, StringComparer.Ordinal)
                .ToList();
        }

        public void Save()
        {
            try
            {
                EnsureCollection();
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = Serialize(_collection);
                File.WriteAllText(_path, json, new UTF8Encoding(false));
                _logInfo?.Invoke("belly store saved count=" + _collection.Profiles.Count + " path=" + _path);
            }
            catch (Exception ex)
            {
                _logWarn?.Invoke("belly store save failed: " + ex.Message);
            }
        }

        private BellyBokoProfileCollection LoadOrCreate()
        {
            try
            {
                if (!File.Exists(_path))
                    return new BellyBokoProfileCollection();

                string json = File.ReadAllText(_path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return new BellyBokoProfileCollection();

                BellyBokoProfileCollection loaded = Deserialize(json);
                if (loaded == null || loaded.Profiles == null)
                    return new BellyBokoProfileCollection();

                loaded.Profiles = loaded.Profiles
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.AnimationKey))
                    .OrderBy(x => x.AnimationKey, StringComparer.Ordinal)
                    .ToList();

                _logInfo?.Invoke("belly store loaded count=" + loaded.Profiles.Count + " path=" + _path);
                return loaded;
            }
            catch (Exception ex)
            {
                _logWarn?.Invoke("belly store load failed: " + ex.Message);
                return new BellyBokoProfileCollection();
            }
        }

        private void EnsureCollection()
        {
            if (_collection == null)
                _collection = new BellyBokoProfileCollection();
            if (_collection.Profiles == null)
                _collection.Profiles = new List<BellyBokoProfile>();
        }

        private static BellyBokoProfile Clone(BellyBokoProfile src)
        {
            return new BellyBokoProfile
            {
                AnimationKey = src.AnimationKey,
                PostureId = src.PostureId,
                PostureMode = src.PostureMode,
                PostureName = src.PostureName,
                MotionStrength = src.MotionStrength,
                DistanceMinMeters = src.DistanceMinMeters,
                DistanceMaxMeters = src.DistanceMaxMeters,
                DistanceCutPercent = src.DistanceCutPercent,
                DistanceSmoothing = src.DistanceSmoothing,
                EaseUp = src.EaseUp,
                EaseDown = src.EaseDown,
                MinInflationSize = src.MinInflationSize,
                MaxInflationSize = src.MaxInflationSize
            };
        }

        private static string Serialize(BellyBokoProfileCollection value)
        {
            if (value == null)
                value = new BellyBokoProfileCollection();

            var serializer = new DataContractJsonSerializer(typeof(BellyBokoProfileCollection));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static BellyBokoProfileCollection Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new BellyBokoProfileCollection();

            var serializer = new DataContractJsonSerializer(typeof(BellyBokoProfileCollection));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                object obj = serializer.ReadObject(ms);
                return obj as BellyBokoProfileCollection ?? new BellyBokoProfileCollection();
            }
        }
    }
}
