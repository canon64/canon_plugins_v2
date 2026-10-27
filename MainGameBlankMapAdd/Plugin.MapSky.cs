using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private sealed class MapSkyChoice
        {
            internal string Id;
            internal string Label;
            internal string Type;
            internal string[] Files;
            internal Color Ambient;
            internal string Source;
        }

        private sealed class MapSkySettings
        {
            internal ConfigEntry<string> Preset;
            internal ConfigEntry<float> AmbientIntensity;
            internal ConfigEntry<float> ReflectionIntensity;
            internal ConfigEntry<bool> UseSkyboxGI;
        }

        private readonly List<MapSkyChoice> _mapSkyChoices = new List<MapSkyChoice>();
        private readonly Dictionary<string, MapSkySettings> _mapSkySettings = new Dictionary<string, MapSkySettings>();
        private string _mapSkyLibraryDirectory;
        private string _mapSkySearch = "";
        private string _activeMapSkyKey;
        private string _activeMapSkyId;
        private float _activeMapSkyAmbientIntensity;
        private float _activeMapSkyReflectionIntensity;
        private bool _activeMapSkyUseSkyboxGI;
        private Material _activeMapSkyMaterial;
        private Texture _activeMapSkyTexture;
        private bool _mapSkyEnvironmentSaved;
        private Material _mapSkyOldMaterial;
        private AmbientMode _mapSkyOldAmbientMode;
        private Color _mapSkyOldAmbientColor;
        private float _mapSkyOldAmbientIntensity;
        private float _mapSkyOldReflectionIntensity;
        private DefaultReflectionMode _mapSkyOldReflectionMode;
        private string _mapSkyFailedKey;
        private string _mapSkyFailedId;
        private readonly Dictionary<GameObject, bool> _mapSkyOccludedHotelObjects =
            new Dictionary<GameObject, bool>();

        private void InitializeMapSkyLibrary()
        {
            _mapSkyLibraryDirectory = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location),
                "Assets", "SkyLibrary");
            string manifest = Path.Combine(_mapSkyLibraryDirectory, "catalog.tsv");
            if (!File.Exists(manifest))
            {
                LogWarn("map sky library catalog missing: " + manifest);
                return;
            }
            foreach (string line in File.ReadAllLines(manifest))
            {
                if (line.StartsWith("id\t", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line)) continue;
                string[] fields = line.Split('\t');
                if (fields.Length < 6 || fields[0].Length == 0) continue;
                Color ambient;
                if (!ColorUtility.TryParseHtmlString(fields[4], out ambient)) ambient = Color.gray;
                string[] files = fields[3].Split(',');
                bool present = true;
                foreach (string file in files)
                    if (!File.Exists(Path.Combine(_mapSkyLibraryDirectory, file))) present = false;
                if (!present) continue;
                _mapSkyChoices.Add(new MapSkyChoice
                {
                    Id = fields[0], Label = fields[1], Type = fields[2], Files = files,
                    Ambient = ambient, Source = fields[5]
                });
            }
            LogInfo("map sky library ready: " + _mapSkyChoices.Count + " choices");
        }

        private string CurrentMapSkyKey()
        {
            if (string.IsNullOrEmpty(_roomOverlaySceneName)) return "original";
            if (_roomOverlaySceneName == VamHotelSceneName) return "hotel";
            if (_roomOverlaySceneName == StreetlightRoadSceneKey) return "streetlight-road";
            return _roomOverlayPositionKey;
        }

        private MapSkySettings GetMapSkySettings(string key)
        {
            MapSkySettings settings;
            if (_mapSkySettings.TryGetValue(key, out settings)) return settings;
            string section = "12.MapSky." + key;
            settings = new MapSkySettings
            {
                Preset = Config.Bind(section, "Preset", "none", "このマップで使う空と環境光のID"),
                AmbientIntensity = Config.Bind(section, "AmbientIntensity", .7f, "このマップの環境光の強度"),
                ReflectionIntensity = Config.Bind(section, "ReflectionIntensity", 1f, "このマップの環境反射の強度"),
                UseSkyboxGI = Config.Bind(section, "UseSkyboxGI", true, "Skyboxから環境光を更新する。オフは画像の代表色を使う")
            };
            _mapSkySettings.Add(key, settings);
            return settings;
        }

        private void TickMapSky()
        {
            string key = CurrentMapSkyKey();
            if (string.IsNullOrEmpty(key)) { ReleaseMapSkyEnvironment(); return; }
            MapSkySettings settings = GetMapSkySettings(key);
            string id = settings.Preset.Value;
            if (string.IsNullOrEmpty(id) || id == "none") { ReleaseMapSkyEnvironment(); return; }
            MapSkyChoice choice = _mapSkyChoices.Find(item => item.Id == id);
            if (choice == null) { ReleaseMapSkyEnvironment(); return; }
            if (_mapSkyFailedKey == key && _mapSkyFailedId == id) return;
            if (_mapSkyFailedKey != key || _mapSkyFailedId != id)
            {
                _mapSkyFailedKey = null;
                _mapSkyFailedId = null;
            }
            float ambient = Mathf.Clamp(settings.AmbientIntensity.Value, 0f, 4f);
            float reflection = Mathf.Clamp(settings.ReflectionIntensity.Value, 0f, 4f);
            bool useSkyboxGI = settings.UseSkyboxGI.Value;
            if (_activeMapSkyMaterial != null && _activeMapSkyKey == key && _activeMapSkyId == id &&
                _activeMapSkyAmbientIntensity == ambient && _activeMapSkyReflectionIntensity == reflection &&
                _activeMapSkyUseSkyboxGI == useSkyboxGI &&
                RenderSettings.skybox == _activeMapSkyMaterial)
            {
                SyncMapSkyOccluders();
                return;
            }
            if (_activeMapSkyId != id || _activeMapSkyKey != key) ReleaseMapSkyEnvironment();
            if (_activeMapSkyMaterial == null)
            {
                try
                {
                    _activeMapSkyMaterial = LoadMapSkyMaterial(choice);
                    if (!_mapSkyEnvironmentSaved)
                    {
                        _mapSkyOldMaterial = RenderSettings.skybox;
                        _mapSkyOldAmbientMode = RenderSettings.ambientMode;
                        _mapSkyOldAmbientColor = RenderSettings.ambientLight;
                        _mapSkyOldAmbientIntensity = RenderSettings.ambientIntensity;
                        _mapSkyOldReflectionIntensity = RenderSettings.reflectionIntensity;
                        _mapSkyOldReflectionMode = RenderSettings.defaultReflectionMode;
                        _mapSkyEnvironmentSaved = true;
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("map sky load failed " + id + ": " + ex);
                    ReleaseMapSkyEnvironment();
                    _mapSkyFailedKey = key;
                    _mapSkyFailedId = id;
                    return;
                }
            }
            RenderSettings.skybox = _activeMapSkyMaterial;
            RenderSettings.ambientMode = useSkyboxGI ? AmbientMode.Skybox : AmbientMode.Flat;
            RenderSettings.ambientLight = choice.Ambient * ambient;
            RenderSettings.ambientIntensity = ambient;
            RenderSettings.reflectionIntensity = reflection;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            try { DynamicGI.UpdateEnvironment(); }
            catch (Exception ex) { LogWarn("map sky GI update failed: " + ex.Message); }
            _activeMapSkyKey = key;
            _activeMapSkyId = id;
            _activeMapSkyAmbientIntensity = ambient;
            _activeMapSkyReflectionIntensity = reflection;
            _activeMapSkyUseSkyboxGI = useSkyboxGI;
            SyncMapSkyOccluders();
        }

        private Material LoadMapSkyMaterial(MapSkyChoice choice)
        {
            if (choice.Type == "panorama")
            {
                Shader shader = Shader.Find("Skybox/Panoramic");
                if (shader == null) throw new InvalidOperationException("Skybox/Panoramic shader missing");
                Texture2D image = new Texture2D(2, 2, TextureFormat.RGB24, true);
                _activeMapSkyTexture = image;
                if (!ImageConversion.LoadImage(image, File.ReadAllBytes(Path.Combine(_mapSkyLibraryDirectory, choice.Files[0]))))
                    throw new InvalidOperationException("panorama image decode failed");
                image.wrapMode = TextureWrapMode.Repeat;
                Material material = new Material(shader) { name = "Map sky: " + choice.Label };
                material.SetTexture("_MainTex", image);
                if (material.HasProperty("_Exposure")) material.SetFloat("_Exposure", 1f);
                return material;
            }
            if (choice.Type == "cubemap" && choice.Files.Length == 6)
            {
                Shader shader = Shader.Find("Skybox/Cubemap");
                if (shader == null) throw new InvalidOperationException("Skybox/Cubemap shader missing");
                Cubemap cube = new Cubemap(1024, TextureFormat.RGB24, false);
                _activeMapSkyTexture = cube;
                CubemapFace[] faces = { CubemapFace.PositiveZ, CubemapFace.NegativeZ,
                    CubemapFace.NegativeX, CubemapFace.PositiveX,
                    CubemapFace.PositiveY, CubemapFace.NegativeY };
                for (int i = 0; i < 6; i++)
                {
                    Texture2D image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        if (!ImageConversion.LoadImage(image, File.ReadAllBytes(Path.Combine(_mapSkyLibraryDirectory, choice.Files[i]))))
                            throw new InvalidOperationException("cubemap face decode failed: " + choice.Files[i]);
                        cube.SetPixels(image.GetPixels(), faces[i]);
                    }
                    finally { Destroy(image); }
                }
                cube.Apply(false);
                Material material = new Material(shader) { name = "Map cube sky: " + choice.Label };
                material.SetTexture("_Tex", cube);
                if (material.HasProperty("_Exposure")) material.SetFloat("_Exposure", 1f);
                return material;
            }
            throw new InvalidOperationException("unsupported sky type: " + choice.Type);
        }

        private void SyncMapSkyOccluders()
        {
            if (_streetlightRoadBackdrop != null)
                _streetlightRoadBackdrop.SetActive(_activeMapSkyMaterial == null);
            if (_activeMapSkyMaterial != null && _roomOverlaySceneName == VamHotelSceneName &&
                _mapSkyOccludedHotelObjects.Count == 0)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(VamHotelSceneName);
                if (scene.IsValid() && scene.isLoaded)
                    foreach (GameObject root in scene.GetRootGameObjects())
                        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                            if (part != null && part.name.StartsWith("Skybox_", StringComparison.OrdinalIgnoreCase))
                                _mapSkyOccludedHotelObjects[part.gameObject] = part.gameObject.activeSelf;
            }
            foreach (var item in _mapSkyOccludedHotelObjects)
                if (item.Key != null) item.Key.SetActive(_activeMapSkyMaterial == null ? item.Value : false);
            if (_activeMapSkyMaterial == null) _mapSkyOccludedHotelObjects.Clear();
        }

        private void ReleaseMapSkyEnvironment()
        {
            if (_mapSkyEnvironmentSaved)
            {
                RenderSettings.skybox = _mapSkyOldMaterial;
                RenderSettings.ambientMode = _mapSkyOldAmbientMode;
                RenderSettings.ambientLight = _mapSkyOldAmbientColor;
                RenderSettings.ambientIntensity = _mapSkyOldAmbientIntensity;
                RenderSettings.reflectionIntensity = _mapSkyOldReflectionIntensity;
                RenderSettings.defaultReflectionMode = _mapSkyOldReflectionMode;
                try { DynamicGI.UpdateEnvironment(); }
                catch (Exception ex) { LogWarn("map sky restore GI update failed: " + ex.Message); }
                _mapSkyEnvironmentSaved = false;
            }
            if (_activeMapSkyMaterial != null) Destroy(_activeMapSkyMaterial);
            if (_activeMapSkyTexture != null) Destroy(_activeMapSkyTexture);
            _activeMapSkyMaterial = null;
            _activeMapSkyTexture = null;
            _activeMapSkyId = null;
            _activeMapSkyKey = null;
            SyncMapSkyOccluders();
        }

        private void DrawMapSkyControls()
        {
            string key = CurrentMapSkyKey();
            if (string.IsNullOrEmpty(key)) return;
            MapSkySettings settings = GetMapSkySettings(key);
            GUILayout.Space(14f);
            GUILayout.Label("空・環境光（このマップごとに保存）");
            GUILayout.Label("現在: " + (settings.Preset.Value == "none" ? "元の空" :
                (_mapSkyChoices.Find(item => item.Id == settings.Preset.Value)?.Label ?? "アセット未配置")));
            bool useSkyboxGI = GUILayout.Toggle(settings.UseSkyboxGI.Value, "Skybox環境光を使用（オフ: 画像の代表色で照らす）");
            if (useSkyboxGI != settings.UseSkyboxGI.Value)
            {
                settings.UseSkyboxGI.Value = useSkyboxGI;
                Config.Save();
                TickMapSky();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("環境光", GUILayout.Width(70f));
            float ambient = GUILayout.HorizontalSlider(settings.AmbientIntensity.Value, 0f, 4f, GUILayout.Width(270f));
            GUILayout.Label(ambient.ToString("0.00", CultureInfo.InvariantCulture), GUILayout.Width(50f));
            if (Mathf.Abs(ambient - settings.AmbientIntensity.Value) > .001f)
            {
                settings.AmbientIntensity.Value = ambient;
                Config.Save();
                TickMapSky();
            }
            GUILayout.Label("反射", GUILayout.Width(40f));
            float reflection = GUILayout.HorizontalSlider(settings.ReflectionIntensity.Value, 0f, 4f, GUILayout.Width(200f));
            GUILayout.Label(reflection.ToString("0.00", CultureInfo.InvariantCulture), GUILayout.Width(50f));
            if (Mathf.Abs(reflection - settings.ReflectionIntensity.Value) > .001f)
            {
                settings.ReflectionIntensity.Value = reflection;
                Config.Save();
                TickMapSky();
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("元の空・環境光に戻す", GUILayout.Width(180f)))
            {
                settings.Preset.Value = "none";
                Config.Save();
                TickMapSky();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("検索", GUILayout.Width(42f));
            _mapSkySearch = GUILayout.TextField(_mapSkySearch, GUILayout.Width(300f));
            GUILayout.Label(_mapSkyChoices.Count + " 種類", GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            foreach (MapSkyChoice choice in _mapSkyChoices)
            {
                if (_mapSkySearch.Length > 0 && choice.Label.IndexOf(_mapSkySearch,
                    StringComparison.OrdinalIgnoreCase) < 0) continue;
                Color old = GUI.backgroundColor;
                if (settings.Preset.Value == choice.Id) GUI.backgroundColor = new Color(.6f, .85f, 1f);
                if (GUILayout.Button(choice.Label + (choice.Type == "cubemap" ? " [Cube]" : " [Panorama]"),
                    GUILayout.MaxWidth(530f)))
                {
                    settings.Preset.Value = choice.Id;
                    Config.Save();
                    TickMapSky();
                }
                GUI.backgroundColor = old;
            }
        }
    }
}
