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
    internal sealed class PregnancyPlusPreset
    {
        [DataMember(Order = 1)]
        public int Slot = 1;
        [DataMember(Order = 2)]
        public string Name = string.Empty;
        [DataMember(Order = 3)]
        public bool GameplayEnabled = true;
        [DataMember(Order = 4)]
        public float InflationSize = 0f;
        [DataMember(Order = 5)]
        public float InflationMoveY = 0f;
        [DataMember(Order = 6)]
        public float InflationMoveZ = 0f;
        [DataMember(Order = 7)]
        public float InflationStretchX = 0f;
        [DataMember(Order = 8)]
        public float InflationStretchY = 0f;
        [DataMember(Order = 9)]
        public float InflationShiftY = 0f;
        [DataMember(Order = 10)]
        public float InflationShiftZ = 0f;
        [DataMember(Order = 11)]
        public float InflationTaperY = 0f;
        [DataMember(Order = 12)]
        public float InflationTaperZ = 0f;
        [DataMember(Order = 13)]
        public float InflationMultiplier = 0f;
        [DataMember(Order = 14)]
        public float InflationClothOffset = 0f;
        [DataMember(Order = 15)]
        public float InflationFatFold = 0f;
        [DataMember(Order = 16)]
        public float InflationFatFoldHeight = 0f;
        [DataMember(Order = 17)]
        public float InflationFatFoldGap = 0f;
        [DataMember(Order = 18)]
        public float InflationRoundness = 0f;
        [DataMember(Order = 19)]
        public float InflationDrop = 0f;
        [DataMember(Order = 20)]
        public int ClothingOffsetVersion = 1;
        [DataMember(Order = 21)]
        public string PluginVersion = string.Empty;
    }

    [DataContract]
    internal sealed class PregnancyPlusPresetCollection
    {
        [DataMember(Order = 1)]
        public List<PregnancyPlusPreset> Presets = new List<PregnancyPlusPreset>();
    }

    internal sealed class PresetStore
    {
        private readonly string _path;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarn;
        private PregnancyPlusPresetCollection _collection;

        public PresetStore(string path, Action<string> logInfo, Action<string> logWarn)
        {
            _path = path;
            _logInfo = logInfo;
            _logWarn = logWarn;
            _collection = LoadOrCreate();
        }

        public bool TryGet(int slot, out PregnancyPlusPreset preset)
        {
            preset = null;
            if (_collection == null || _collection.Presets == null)
                return false;

            PregnancyPlusPreset found = _collection.Presets.FirstOrDefault(x => x != null && x.Slot == slot);
            if (found == null)
                return false;

            preset = Clone(found);
            return true;
        }

        public void Upsert(PregnancyPlusPreset preset)
        {
            if (preset == null)
                return;

            EnsureCollection();
            for (int i = _collection.Presets.Count - 1; i >= 0; i--)
            {
                PregnancyPlusPreset current = _collection.Presets[i];
                if (current != null && current.Slot == preset.Slot)
                    _collection.Presets.RemoveAt(i);
            }

            _collection.Presets.Add(Clone(preset));
            _collection.Presets = _collection.Presets
                .Where(x => x != null)
                .OrderBy(x => x.Slot)
                .ToList();
        }

        public void Save()
        {
            try
            {
                EnsureCollection();
                int countBefore = _collection.Presets != null ? _collection.Presets.Count : 0;
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = Serialize(_collection);
                string jsonHead = json == null ? "null" : (json.Length > 80 ? json.Substring(0, 80) : json);
                _logInfo?.Invoke("preset save diag: countBefore=" + countBefore + " jsonLen=" + (json == null ? -1 : json.Length) + " jsonHead=" + (jsonHead ?? string.Empty));
                File.WriteAllText(_path, json, new UTF8Encoding(false));

                long fileSize = -1;
                try { fileSize = new FileInfo(_path).Length; } catch { }

                int countReload = -1;
                try
                {
                    string re = File.ReadAllText(_path, Encoding.UTF8);
                    PregnancyPlusPresetCollection loaded = Deserialize(re);
                    countReload = loaded?.Presets != null ? loaded.Presets.Count : 0;
                }
                catch
                {
                    countReload = -2;
                }

                _logInfo?.Invoke("preset save diag: fileSize=" + fileSize + " reloadCount=" + countReload + " path=" + _path);
            }
            catch (Exception ex)
            {
                _logWarn?.Invoke("preset save failed: " + ex.Message);
            }
        }

        private PregnancyPlusPresetCollection LoadOrCreate()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    PregnancyPlusPresetCollection empty = new PregnancyPlusPresetCollection();
                    string dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(_path, Serialize(empty), new UTF8Encoding(false));
                    return empty;
                }

                string json = File.ReadAllText(_path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return new PregnancyPlusPresetCollection();

                PregnancyPlusPresetCollection loaded = Deserialize(json);
                if (loaded == null || loaded.Presets == null)
                    return new PregnancyPlusPresetCollection();

                loaded.Presets = loaded.Presets.Where(x => x != null).OrderBy(x => x.Slot).ToList();
                return loaded;
            }
            catch (Exception ex)
            {
                _logWarn?.Invoke("preset load failed: " + ex.Message);
                return new PregnancyPlusPresetCollection();
            }
        }

        private void EnsureCollection()
        {
            if (_collection == null)
                _collection = new PregnancyPlusPresetCollection();
            if (_collection.Presets == null)
                _collection.Presets = new List<PregnancyPlusPreset>();
        }

        private static PregnancyPlusPreset Clone(PregnancyPlusPreset src)
        {
            return new PregnancyPlusPreset
            {
                Slot = src.Slot,
                Name = src.Name,
                GameplayEnabled = src.GameplayEnabled,
                InflationSize = src.InflationSize,
                InflationMoveY = src.InflationMoveY,
                InflationMoveZ = src.InflationMoveZ,
                InflationStretchX = src.InflationStretchX,
                InflationStretchY = src.InflationStretchY,
                InflationShiftY = src.InflationShiftY,
                InflationShiftZ = src.InflationShiftZ,
                InflationTaperY = src.InflationTaperY,
                InflationTaperZ = src.InflationTaperZ,
                InflationMultiplier = src.InflationMultiplier,
                InflationClothOffset = src.InflationClothOffset,
                InflationFatFold = src.InflationFatFold,
                InflationFatFoldHeight = src.InflationFatFoldHeight,
                InflationFatFoldGap = src.InflationFatFoldGap,
                InflationRoundness = src.InflationRoundness,
                InflationDrop = src.InflationDrop,
                ClothingOffsetVersion = src.ClothingOffsetVersion,
                PluginVersion = src.PluginVersion
            };
        }

        private static string Serialize(PregnancyPlusPresetCollection value)
        {
            if (value == null)
                value = new PregnancyPlusPresetCollection();

            var serializer = new DataContractJsonSerializer(typeof(PregnancyPlusPresetCollection));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static PregnancyPlusPresetCollection Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new PregnancyPlusPresetCollection();

            var serializer = new DataContractJsonSerializer(typeof(PregnancyPlusPresetCollection));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                object obj = serializer.ReadObject(ms);
                return obj as PregnancyPlusPresetCollection ?? new PregnancyPlusPresetCollection();
            }
        }
    }
}
