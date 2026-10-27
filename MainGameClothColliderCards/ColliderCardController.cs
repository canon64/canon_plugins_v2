using System;
using System.Collections.Generic;
using System.IO;
using ExtensibleSaveFormat;
using KKAPI;
using KKAPI.Chara;
using UnityEngine;

namespace MainGameClothColliderCards
{
    public enum ColliderShape : byte { Sphere = 1, Capsule = 2 }

    public sealed class ColliderRecord
    {
        public string BonePath;
        public ColliderShape Shape;
        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;
        public float Radius;
        public float Height;
        public int Direction;

        public ColliderRecord Copy() => (ColliderRecord)MemberwiseClone();
    }

    // The card contains definitions, not Unity object references. Objects are rebuilt for each ChaControl.
    public sealed partial class ColliderCardController : CharaCustomFunctionController
    {
        private const string DataKey = "colliders";
        private const string FlareSkirtKey = "flareSkirtACloth";
        private const int DataVersion = 1;
        private const int MaxColliders = 128;
        private readonly List<ColliderRecord> _records = new List<ColliderRecord>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private bool _flareSkirtACloth;
        private bool _hasFlareClothSetting;
        private ClothTuning _clothTuning = new ClothTuning();
        private bool _clothTuningEdited;

        public IReadOnlyList<ColliderRecord> Records => _records.AsReadOnly();
        public bool FlareSkirtACloth => _flareSkirtACloth;
        public ClothTuning CurrentClothTuning => _clothTuning.Copy();
        public bool HasActiveCloth => _flareClones.Count > 0;

        public void SetClothTuning(ClothTuning tuning)
        {
            if (tuning == null) return;
            tuning = tuning.Copy();
            tuning.Clamp();
            if (_clothTuning.SameAs(tuning)) return;
            _clothTuning = tuning;
            _clothTuningEdited = true;
            foreach (var clone in _flareClones)
            {
                if (clone == null) continue;
                var cloth = clone.GetComponent<Cloth>();
                var renderer = clone.GetComponent<SkinnedMeshRenderer>();
                if (cloth != null && renderer != null && renderer.sharedMesh != null)
                    ApplyClothTuning(cloth, renderer.sharedMesh);
            }
            SaveData();
        }

        public void SetFlareSkirtACloth(bool enabled)
        {
            if (_hasFlareClothSetting && _flareSkirtACloth == enabled) return;
            _flareSkirtACloth = enabled;
            _hasFlareClothSetting = true;
            RefreshFlareSkirt(force: true);
            SaveData();
        }

        // A relative path from ChaControl.transform, e.g. obtained with GetBonePath().
        public static string GetBonePath(ChaControl character, Transform bone)
        {
            if (character == null || bone == null) return null;
            var names = new List<string>();
            Transform cursor = bone;
            while (cursor != null && cursor != character.transform)
            {
                names.Add(cursor.name);
                cursor = cursor.parent;
            }
            if (cursor == null) return null;
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        public bool AddCollider(ColliderRecord record)
        {
            if (!Valid(record) || _records.Count >= MaxColliders || FindBone(record.BonePath) == null) return false;
            var copy = record.Copy();
            _records.Add(copy);
            _objects.Add(Create(copy));
            RefreshFlareSkirt(force: true);
            SaveData();
            return true;
        }

        public bool ReplaceCollider(int index, ColliderRecord record)
        {
            if (index < 0 || index >= _records.Count || !Valid(record) || FindBone(record.BonePath) == null) return false;
            _records[index] = record.Copy();
            Rebuild();
            RefreshFlareSkirt(force: true);
            SaveData();
            return true;
        }

        public bool RemoveCollider(int index)
        {
            if (index < 0 || index >= _records.Count) return false;
            _records.RemoveAt(index);
            Rebuild();
            RefreshFlareSkirt(force: true);
            SaveData();
            return true;
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            CaptureObjects();
            SaveData();
        }

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
            ClearFlareSkirt();
            ClearObjects();
            _records.Clear();
            _flareSkirtACloth = false;
            _hasFlareClothSetting = false;
            _clothTuning = new ClothTuning();
            _clothTuningEdited = false;
            try
            {
                PluginData data = GetExtendedData(false) ?? GetExtendedData();
                Plugin.LogSource.LogInfo("Extended card data=" +
                          (data == null ? "null" : "version=" + data.version));
                if (data != null && data.version == DataVersion)
                {
                    if (data.data.TryGetValue(DataKey, out var value) && value is byte[] bytes)
                        _records.AddRange(Decode(bytes));
                    if (data.data.TryGetValue(FlareSkirtKey, out var flare))
                    {
                        _hasFlareClothSetting = true;
                        _flareSkirtACloth = flare is bool flag && flag;
                    }
                    ReadClothTuning(data);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning("Invalid card data: " + ex.Message);
            }
            Rebuild();
            Plugin.LogSource.LogInfo("Card reload character=" +
                      (ChaControl == null ? "null" : ChaControl.name) +
                      " clothEnabled=" + _flareSkirtACloth +
                      " colliderRecords=" + _records.Count);
            RefreshFlareSkirt(force: true);
        }

        protected override void OnDestroy()
        {
            ClearFlareSkirt();
            ClearObjects();
            base.OnDestroy();
        }

        private Transform FindBone(string path)
        {
            if (ChaControl == null || string.IsNullOrEmpty(path)) return null;
            var found = ChaControl.transform.Find(path);
            if (found != null || path.Contains("/")) return found;
            foreach (var bone in ChaControl.GetComponentsInChildren<Transform>(true))
                if (string.Equals(bone.name, path, StringComparison.OrdinalIgnoreCase)) return bone;
            return null;
        }

        private void Rebuild()
        {
            ClearObjects();
            foreach (var record in _records) _objects.Add(Create(record));
        }

        private GameObject Create(ColliderRecord record)
        {
            Transform bone = FindBone(record.BonePath);
            if (bone == null) return null;
            var obj = new GameObject("ClothColliderCards");
            obj.transform.SetParent(bone, false);
            obj.transform.localPosition = record.LocalPosition;
            obj.transform.localRotation = Quaternion.Euler(record.LocalEulerAngles);
            if (record.Shape == ColliderShape.Sphere)
            {
                obj.AddComponent<SphereCollider>().radius = record.Radius;
            }
            else
            {
                var capsule = obj.AddComponent<CapsuleCollider>();
                capsule.radius = record.Radius;
                capsule.height = record.Height;
                capsule.direction = record.Direction;
            }
            return obj;
        }

        private void ClearObjects()
        {
            foreach (var obj in _objects)
                if (obj != null) Destroy(obj);
            _objects.Clear();
        }

        private void CaptureObjects()
        {
            for (int i = 0; i < _records.Count && i < _objects.Count; i++)
            {
                var obj = _objects[i];
                if (obj == null) continue;
                var record = _records[i];
                record.LocalPosition = obj.transform.localPosition;
                record.LocalEulerAngles = obj.transform.localEulerAngles;
                if (record.Shape == ColliderShape.Sphere)
                {
                    var sphere = obj.GetComponent<SphereCollider>();
                    if (sphere != null) record.Radius = sphere.radius;
                }
                else
                {
                    var capsule = obj.GetComponent<CapsuleCollider>();
                    if (capsule != null)
                    {
                        record.Radius = capsule.radius;
                        record.Height = capsule.height;
                        record.Direction = capsule.direction;
                    }
                }
            }
        }

        private void SaveData()
        {
            if (_records.Count == 0 && !_flareSkirtACloth && !_hasFlareClothSetting && !_clothTuningEdited)
            { SetExtendedData(null); return; }
            foreach (var record in _records)
                if (!Valid(record)) throw new InvalidDataException("Invalid collider settings");
            var data = new PluginData { version = DataVersion };
            data.data[DataKey] = Encode(_records);
            data.data[FlareSkirtKey] = _flareSkirtACloth;
            data.data["pinnedTop"] = _clothTuning.PinnedTop;
            data.data["hemDistance"] = _clothTuning.HemDistance;
            data.data["damping"] = _clothTuning.Damping;
            data.data["friction"] = _clothTuning.Friction;
            data.data["stretch"] = _clothTuning.Stretch;
            data.data["bend"] = _clothTuning.Bend;
            data.data["solverHz"] = _clothTuning.SolverHz;
            data.data["extraDown"] = _clothTuning.ExtraDown;
            data.data["worldVelocity"] = _clothTuning.WorldVelocity;
            data.data["worldAcceleration"] = _clothTuning.WorldAcceleration;
            data.data["gravity"] = _clothTuning.Gravity;
            data.data["useTethers"] = _clothTuning.UseTethers;
            SetExtendedData(data);
        }

        private void ReadClothTuning(PluginData data)
        {
            _clothTuning.PinnedTop = ReadClothFloat(data, "pinnedTop", _clothTuning.PinnedTop);
            _clothTuning.HemDistance = ReadClothFloat(data, "hemDistance", _clothTuning.HemDistance);
            _clothTuning.Damping = ReadClothFloat(data, "damping", _clothTuning.Damping);
            _clothTuning.Friction = ReadClothFloat(data, "friction", _clothTuning.Friction);
            _clothTuning.Stretch = ReadClothFloat(data, "stretch", _clothTuning.Stretch);
            _clothTuning.Bend = ReadClothFloat(data, "bend", _clothTuning.Bend);
            _clothTuning.SolverHz = ReadClothFloat(data, "solverHz", _clothTuning.SolverHz);
            _clothTuning.ExtraDown = ReadClothFloat(data, "extraDown", _clothTuning.ExtraDown);
            _clothTuning.WorldVelocity = ReadClothFloat(data, "worldVelocity", _clothTuning.WorldVelocity);
            _clothTuning.WorldAcceleration = ReadClothFloat(data, "worldAcceleration", _clothTuning.WorldAcceleration);
            if (data.data.TryGetValue("gravity", out var value) && value is bool gravity)
                _clothTuning.Gravity = gravity;
            if (data.data.TryGetValue("useTethers", out var tethers) && tethers is bool useTethers)
                _clothTuning.UseTethers = useTethers;
            _clothTuning.Clamp();
        }

        private static float ReadClothFloat(PluginData data, string key, float fallback)
        {
            if (!data.data.TryGetValue(key, out var value)) return fallback;
            try
            {
                float result = Convert.ToSingle(value);
                return Finite(result) ? result : fallback;
            }
            catch { return fallback; }
        }

        private static byte[] Encode(IList<ColliderRecord> records)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(DataVersion);
                writer.Write(records.Count);
                foreach (var r in records)
                {
                    writer.Write(r.BonePath);
                    writer.Write((byte)r.Shape);
                    WriteVector(writer, r.LocalPosition);
                    WriteVector(writer, r.LocalEulerAngles);
                    writer.Write(r.Radius);
                    writer.Write(r.Height);
                    writer.Write(r.Direction);
                }
                return stream.ToArray();
            }
        }

        private static List<ColliderRecord> Decode(byte[] bytes)
        {
            var result = new List<ColliderRecord>();
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != DataVersion) throw new InvalidDataException("Version");
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxColliders) throw new InvalidDataException("Count");
                for (int i = 0; i < count; i++)
                {
                    var r = new ColliderRecord
                    {
                        BonePath = reader.ReadString(),
                        Shape = (ColliderShape)reader.ReadByte(),
                        LocalPosition = ReadVector(reader),
                        LocalEulerAngles = ReadVector(reader),
                        Radius = reader.ReadSingle(),
                        Height = reader.ReadSingle(),
                        Direction = reader.ReadInt32()
                    };
                    if (!Valid(r)) throw new InvalidDataException("Collider " + i);
                    result.Add(r);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing bytes");
            }
            return result;
        }

        private static void WriteVector(BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x); writer.Write(v.y); writer.Write(v.z);
        }

        private static Vector3 ReadVector(BinaryReader reader) =>
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static bool Valid(ColliderRecord r)
        {
            return r != null && !string.IsNullOrEmpty(r.BonePath) && r.BonePath.Length <= 512 &&
                   !r.BonePath.StartsWith("/") && !r.BonePath.Contains("..") &&
                   (r.Shape == ColliderShape.Sphere || r.Shape == ColliderShape.Capsule) &&
                   Finite(r.LocalPosition) && Finite(r.LocalEulerAngles) &&
                   Finite(r.Radius) && r.Radius > 0 && r.Radius <= 10 &&
                   Finite(r.Height) && r.Height >= 0 && r.Height <= 20 &&
                   r.Direction >= 0 && r.Direction <= 2;
        }

        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
