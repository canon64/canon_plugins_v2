using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        // ---------------- White Room ----------------
        // アストラル側の基準点は、BlankMapAdd の動画ルームがあればその中心へ合わせる。
        // 見つからない場合だけ従来の遠方座標へフォールバックする。

        private static readonly Vector3 FallbackAstralWorldOrigin = new Vector3(0f, 10000f, 0f);
        private const float RoomSize = 30f;

        private GameObject _roomRoot;
        private Light _roomLight;

        internal Vector3 GetAstralWorldOrigin()
        {
            GameObject videoRoom = GameObject.Find("__BlankMapVideoRoom");
            if (videoRoom != null)
            {
                Vector3 pos = videoRoom.transform.position;
                SimpleFileLogger.Info($"AstralWorldOrigin resolved from BlankMap video room: pos={pos}, active={videoRoom.activeInHierarchy}, parent={(videoRoom.transform.parent != null ? videoRoom.transform.parent.name : "null")}");
                return pos;
            }

            SimpleFileLogger.Warn($"AstralWorldOrigin fallback used. __BlankMapVideoRoom not found. fallback={FallbackAstralWorldOrigin}");
            return FallbackAstralWorldOrigin;
        }

        private Vector3 GetWhiteRoomCenter()
        {
            Vector3 center = GetAstralCharaCenter();
            if (center == Vector3.zero)
            {
                center = GetAstralWorldOrigin();
                SimpleFileLogger.Warn($"White room center fallback used. astralCharaCenter was zero. center={center}");
            }
            else
            {
                SimpleFileLogger.Info($"White room center resolved from astral character center: center={center}");
            }
            return center;
        }

        internal void CreateWhiteRoom()
        {
            if (_cfgShowWhiteRoom != null && !_cfgShowWhiteRoom.Value)
            {
                if (_roomRoot != null) _roomRoot.SetActive(false);
                SimpleFileLogger.Info("White room skipped by config");
                return;
            }

            Vector3 origin = GetWhiteRoomCenter();

            if (_roomRoot != null)
            {
                _roomRoot.transform.position = origin;
                _roomRoot.SetActive(true);
                SimpleFileLogger.Info($"White room reused at {origin}");
                return;
            }

            _roomRoot = new GameObject("AstralMode_WhiteRoom");
            Object.DontDestroyOnLoad(_roomRoot);
            _roomRoot.transform.position = origin;
            _roomRoot.hideFlags = HideFlags.DontSave;

            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            Material whiteMat = new Material(shader)
            {
                hideFlags = HideFlags.DontSave,
                color = Color.white
            };
            if (whiteMat.HasProperty("_EmissionColor"))
            {
                whiteMat.EnableKeyword("_EMISSION");
                whiteMat.SetColor("_EmissionColor", new Color(0.8f, 0.8f, 0.8f));
            }

            float half = RoomSize * 0.5f;
            float scale = RoomSize / 10f;

            CreateWall(_roomRoot.transform, whiteMat, "Floor",   new Vector3(0,  -half, 0), Quaternion.identity, scale);
            CreateWall(_roomRoot.transform, whiteMat, "Ceiling", new Vector3(0,   half, 0), Quaternion.Euler(180, 0, 0), scale);
            CreateWall(_roomRoot.transform, whiteMat, "North",   new Vector3(0, 0,  half), Quaternion.Euler(-90, 0, 0), scale);
            CreateWall(_roomRoot.transform, whiteMat, "South",   new Vector3(0, 0, -half), Quaternion.Euler(90, 0, 0), scale);
            CreateWall(_roomRoot.transform, whiteMat, "East",    new Vector3( half, 0, 0), Quaternion.Euler(0, 0, 90), scale);
            CreateWall(_roomRoot.transform, whiteMat, "West",    new Vector3(-half, 0, 0), Quaternion.Euler(0, 0, -90), scale);

            GameObject lightGo = new GameObject("RoomLight");
            lightGo.transform.SetParent(_roomRoot.transform, false);
            lightGo.transform.localPosition = Vector3.up * (half - 1f);
            _roomLight = lightGo.AddComponent<Light>();
            _roomLight.type = LightType.Point;
            _roomLight.intensity = 3f;
            _roomLight.range = RoomSize * 1.5f;
            _roomLight.color = Color.white;

            SimpleFileLogger.Info($"White room created at {origin}");
        }

        private void CreateWall(Transform parent, Material mat, string name, Vector3 localPos, Quaternion localRot, float scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = new Vector3(scale, 1f, scale);
            go.hideFlags = HideFlags.DontSave;

            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        internal void DestroyWhiteRoom()
        {
            if (_roomRoot != null)
            {
                _roomRoot.SetActive(false);
                SimpleFileLogger.Info("White room hidden for reuse");
            }
        }

        internal void DisposeWhiteRoom()
        {
            if (_roomRoot != null)
            {
                Object.Destroy(_roomRoot);
                _roomRoot = null;
                _roomLight = null;
                SimpleFileLogger.Info("White room disposed");
            }
        }
    }
}
