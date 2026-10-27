using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private const string StreetlightRoadSceneKey = "custom:streetlight-road";
        private GameObject _streetlightRoadRoot;
        private GameObject _streetlightRoadBackdrop;
        private Light _streetlightRoadSpot;
        private readonly List<Light> _streetlightRoadDisabledLights = new List<Light>();
        private readonly List<Material> _streetlightRoadMaterials = new List<Material>();
        private readonly List<Texture2D> _streetlightRoadTextures = new List<Texture2D>();
        private bool _streetlightRoadEnvironmentSaved;
        private AmbientMode _streetlightRoadOldAmbientMode;
        private Color _streetlightRoadOldAmbientLight;
        private float _streetlightRoadOldAmbientIntensity;
        private Material _streetlightRoadOldSkybox;
        private bool _streetlightRoadOldFog;
        private Color _streetlightRoadOldFogColor;
        private FogMode _streetlightRoadOldFogMode;
        private float _streetlightRoadOldFogDensity;
        private bool _streetlightRoadConfigDirty;
        private ConfigEntry<float> _roadLightIntensity;
        private ConfigEntry<float> _roadLightRange;
        private ConfigEntry<float> _roadLightAngle;
        private ConfigEntry<string> _roadLightColor;
        private ConfigEntry<float> _roadLampHeight;
        private ConfigEntry<float> _roadPoleOffset;
        private ConfigEntry<float> _roadWidth;
        private ConfigEntry<float> _roadLength;
        private ConfigEntry<float> _roadTileX;
        private ConfigEntry<float> _roadTileY;
        private ConfigEntry<float> _roadNormalStrength;
        private ConfigEntry<float> _roadFogDensity;
        private ConfigEntry<string> _roadBackgroundColor;
        private ConfigEntry<bool> _roadCameraPreview;
        private ConfigEntry<float> _roadCameraDistance;
        private ConfigEntry<float> _roadCameraHeight;
        private ConfigEntry<float> _roadCameraTargetHeight;
        private ConfigEntry<float> _roadPoleVisibility;
        private ConfigEntry<float> _roadCameraFov;
        private CameraControl_Ver2 _roadPreviewController;
        private BaseCameraControl_Ver2.CameraData _roadPreviousCameraData;

        private void InitializeStreetlightRoadConfig()
        {
            const string section = "09.StreetlightRoad";
            _roadLightIntensity = WatchRoadSetting(Config.Bind(section, "LightIntensity", 1.15f, "街灯の明るさ"));
            _roadLightRange = WatchRoadSetting(Config.Bind(section, "LightRange", 11f, "街灯の照射距離"));
            _roadLightAngle = WatchRoadSetting(Config.Bind(section, "LightAngle", 66f, "街灯の照射角度"));
            _roadLightColor = WatchRoadSetting(Config.Bind(section, "LightColor", "#FFFFFF", "街灯の照射光と灯具の発光色。#RRGGBB"));
            _roadLampHeight = WatchRoadSetting(Config.Bind(section, "LampHeight", 5.8f, "街灯の高さ"));
            _roadPoleOffset = WatchRoadSetting(Config.Bind(section, "PoleOffset", 2.8f, "人物の右側から街灯までの距離"));
            _roadWidth = WatchRoadSetting(Config.Bind(section, "RoadWidth", 18f, "道路の幅"));
            _roadLength = WatchRoadSetting(Config.Bind(section, "RoadLength", 50f, "道路の長さ"));
            _roadTileX = WatchRoadSetting(Config.Bind(section, "TextureTileX", 2.5f, "道路テクスチャの横の繰り返し"));
            _roadTileY = WatchRoadSetting(Config.Bind(section, "TextureTileY", 7f, "道路テクスチャの縦の繰り返し"));
            _roadNormalStrength = WatchRoadSetting(Config.Bind(section, "NormalStrength", 0.65f, "道路の凹凸の強さ"));
            _roadFogDensity = WatchRoadSetting(Config.Bind(section, "FogDensity", 0.07f, "遠景を黒へ溶かす霧の濃さ"));
            _roadBackgroundColor = WatchRoadSetting(Config.Bind(section, "BackgroundColor", "#000000", "背景色。完全な黒は #000000"));
            _roadCameraPreview = WatchRoadSetting(Config.Bind(section, "CameraPreview", true, "道路と街灯が見えるカメラ位置へ移動する"));
            _roadCameraDistance = WatchRoadSetting(Config.Bind(section, "CameraDistance", 6f, "道路を見るカメラの距離"));
            _roadCameraHeight = WatchRoadSetting(Config.Bind(section, "CameraHeight", 3.2f, "道路を見るカメラの高さ"));
            _roadCameraTargetHeight = WatchRoadSetting(Config.Bind(section, "CameraTargetHeight", 2.5f, "カメラが見る高さ"));
            _roadCameraFov = WatchRoadSetting(Config.Bind(section, "CameraFov", 55f, "道路を見るカメラの視野角"));
            _roadPoleVisibility = WatchRoadSetting(Config.Bind(section, "PoleVisibility", 0.20f, "暗い背景でも支柱が見える最低限の発光量"));
        }

        private ConfigEntry<T> WatchRoadSetting<T>(ConfigEntry<T> entry)
        {
            entry.SettingChanged += (sender, args) => _streetlightRoadConfigDirty = true;
            return entry;
        }

        private static Color RoadColor(string text, Color fallback)
        {
            Color parsed;
            return ColorUtility.TryParseHtmlString(text, out parsed) ? parsed : fallback;
        }

        private void RebuildStreetlightRoadIfNeeded()
        {
            if (!_streetlightRoadConfigDirty || _roomOverlaySceneName != StreetlightRoadSceneKey) return;
            _streetlightRoadConfigDirty = false;
            try
            {
                DestroyStreetlightRoad();
                CreateStreetlightRoad();
                EnforceStreetlightRoadLighting();
                ApplyStreetlightCameraPreview();
            }
            catch (Exception ex) { LogWarn("streetlight road settings failed: " + ex); ClearRoomOverlay(); }
        }

        private void SelectStreetlightRoadOverlay()
        {
            if (!CanUseRoomOverlay() || _roomOverlayLoading) return;
            _roomOverlayDropdownOpen = false;
            StartCoroutine(SwitchToStreetlightRoadOverlay());
        }

        private IEnumerator SwitchToStreetlightRoadOverlay()
        {
            _roomOverlayLoading = true;
            _roomOverlayLoadStartedAt = Time.realtimeSinceStartup;
            yield return UnloadRoomOverlay();
            if (!CanUseRoomOverlay()) { _roomOverlayLoading = false; yield break; }
            try
            {
                CreateStreetlightRoad();
                _roomOverlaySceneName = StreetlightRoadSceneKey;
                _roomOverlayDisplayName = "街灯のある暗い道路";
                HideSourceRoomForOverlay();
                EnforceStreetlightRoadLighting();
                ApplyStreetlightCameraPreview();
                LogInfo("streetlight road shown: asphalt albedo and normal map loaded");
            }
            catch (Exception ex)
            {
                LogWarn("streetlight road creation failed: " + ex);
                DestroyStreetlightRoad();
                RestoreSourceRoomAfterOverlay();
                _roomOverlaySceneName = null;
                _roomOverlayDisplayName = null;
            }
            _roomOverlayLoading = false;
        }

        private void CreateStreetlightRoad()
        {
            ChaControl actor = FindObjectOfType<HSceneProc>() != null
                ? FindObjectsOfType<ChaControl>().FirstOrDefault(x => x != null && x.sex == 1) : null;
            if (actor == null) throw new InvalidOperationException("H actor missing");
            _streetlightRoadRoot = new GameObject("StreetlightRoadOverlay");
            _streetlightRoadRoot.transform.position = actor.transform.position;
            _roomOverlayPositionKey = "streetlight-road";
            _roomOverlayPositionRoot = _streetlightRoadRoot;
            _roomOverlayBasePosition = _streetlightRoadRoot.transform.position;
            _roomOverlayBaseRotation = _streetlightRoadRoot.transform.rotation;
            _roomOverlayActorAnchor = actor.transform.position;
            EnsureRoomOverlayPositionConfig(_roomOverlayPositionKey);
            ApplyRoomOverlayPosition();

            Texture2D asphalt = LoadStreetlightTexture("MainGameBlankMapAdd.StreetlightAsphalt.png");
            Texture2D normal = LoadStreetlightTexture("MainGameBlankMapAdd.StreetlightAsphaltNormal.png");
            asphalt.wrapMode = TextureWrapMode.Mirror;
            normal.wrapMode = TextureWrapMode.Mirror;
            asphalt.filterMode = FilterMode.Trilinear;
            normal.filterMode = FilterMode.Trilinear;
            asphalt.anisoLevel = 8;
            normal.anisoLevel = 8;

            CreateStreetlightRoadBackdrop();

            Shader roadShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            if (roadShader == null) throw new InvalidOperationException("lit road shader missing");
            Material road = new Material(roadShader) { name = "Streetlight asphalt" };
            road.mainTexture = asphalt;
            road.mainTextureScale = new Vector2(Mathf.Clamp(_roadTileX.Value, 0.1f, 40f),
                Mathf.Clamp(_roadTileY.Value, 0.1f, 80f));
            road.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            if (road.HasProperty("_BumpMap"))
            {
                road.SetTexture("_BumpMap", normal);
                road.SetTextureScale("_BumpMap", road.mainTextureScale);
                road.SetFloat("_BumpScale", Mathf.Clamp(_roadNormalStrength.Value, 0f, 2f));
                road.EnableKeyword("_NORMALMAP");
            }
            if (road.HasProperty("_Glossiness")) road.SetFloat("_Glossiness", 0.18f);
            _streetlightRoadMaterials.Add(road);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Textured asphalt road";
            ground.transform.SetParent(_streetlightRoadRoot.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.035f, 0f);
            ground.transform.localScale = new Vector3(Mathf.Clamp(_roadWidth.Value, 2f, 200f) / 10f,
                1f, Mathf.Clamp(_roadLength.Value, 4f, 500f) / 10f);
            Renderer groundRenderer = ground.GetComponent<Renderer>();
            groundRenderer.sharedMaterial = road;
            groundRenderer.lightProbeUsage = LightProbeUsage.Off;
            groundRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Material metal = MakeStreetlightMaterial("Streetlight dark metal", new Color(0.13f, 0.14f, 0.15f));
            if (metal.HasProperty("_EmissionColor"))
            {
                float visibility = Mathf.Clamp(_roadPoleVisibility.Value, 0f, 1f);
                metal.EnableKeyword("_EMISSION");
                metal.SetColor("_EmissionColor", new Color(visibility * 0.87f, visibility * 0.94f, visibility));
            }
            Color lampColor = RoadColor(_roadLightColor.Value, Color.white);
            Material lens = MakeStreetlightMaterial("Streetlight glowing lens", lampColor);
            if (lens.HasProperty("_EmissionColor"))
            {
                lens.EnableKeyword("_EMISSION");
                lens.SetColor("_EmissionColor", lampColor * 1.2f);
            }
            float height = Mathf.Clamp(_roadLampHeight.Value, 2f, 15f);
            float offset = Mathf.Clamp(_roadPoleOffset.Value, 0.5f, 10f);
            MakeStreetlightPart(PrimitiveType.Cylinder, "Single streetlight pole", new Vector3(offset, height * 0.5f, 0f),
                new Vector3(0.19f, height * 0.5f, 0.19f), metal);
            MakeStreetlightPart(PrimitiveType.Cube, "Streetlight arm", new Vector3(offset * 0.5f, height - 0.02f, 0f),
                new Vector3(offset, 0.12f, 0.13f), metal);
            MakeStreetlightPart(PrimitiveType.Cube, "Streetlight lamp housing", new Vector3(0f, height - 0.08f, 0f),
                new Vector3(0.85f, 0.18f, 0.48f), metal);
            MakeStreetlightPart(PrimitiveType.Cube, "Streetlight glowing lens", new Vector3(0f, height - 0.20f, 0f),
                new Vector3(0.68f, 0.04f, 0.34f), lens);

            GameObject lamp = new GameObject("One streetlight spotlight");
            lamp.transform.SetParent(_streetlightRoadRoot.transform, false);
            lamp.transform.localPosition = new Vector3(0f, height - 0.26f, 0f);
            lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _streetlightRoadSpot = lamp.AddComponent<Light>();
            _streetlightRoadSpot.type = LightType.Spot;
            _streetlightRoadSpot.color = lampColor;
            _streetlightRoadSpot.intensity = Mathf.Clamp(_roadLightIntensity.Value, 0f, 8f);
            _streetlightRoadSpot.range = Mathf.Clamp(_roadLightRange.Value, 1f, 50f);
            _streetlightRoadSpot.spotAngle = Mathf.Clamp(_roadLightAngle.Value, 1f, 120f);
            _streetlightRoadSpot.shadows = LightShadows.Hard;
            _streetlightRoadSpot.shadowStrength = 0.85f;

            SaveStreetlightRoadEnvironment();
        }

        private Texture2D LoadStreetlightTexture(string name)
        {
            using (Stream stream = GetType().Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new FileNotFoundException(name);
                byte[] bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new EndOfStreamException(name);
                    offset += read;
                }
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name };
                if (!ImageConversion.LoadImage(texture, bytes, false))
                {
                    Destroy(texture);
                    throw new InvalidDataException(name);
                }
                _streetlightRoadTextures.Add(texture);
                return texture;
            }
        }

        private Material MakeStreetlightMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Material material = new Material(shader) { name = name, color = color };
            _streetlightRoadMaterials.Add(material);
            return material;
        }

        private void MakeStreetlightPart(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(_streetlightRoadRoot.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void CreateStreetlightRoadBackdrop()
        {
            _streetlightRoadBackdrop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _streetlightRoadBackdrop.name = "Streetlight black skydome";
            _streetlightRoadBackdrop.transform.SetParent(_streetlightRoadRoot.transform, false);
            _streetlightRoadBackdrop.transform.localPosition = Vector3.zero;
            _streetlightRoadBackdrop.transform.localScale = Vector3.one * 200f;
            Collider collider = _streetlightRoadBackdrop.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Mesh mesh = _streetlightRoadBackdrop.GetComponent<MeshFilter>().mesh;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int temp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temp;
            }
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("skydome shader missing");
            Material material = new Material(shader) { name = "Streetlight skydome" };
            material.color = RoadColor(_roadBackgroundColor.Value, Color.black);
            _streetlightRoadMaterials.Add(material);
            Renderer renderer = _streetlightRoadBackdrop.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        private void SaveStreetlightRoadEnvironment()
        {
            _streetlightRoadOldAmbientMode = RenderSettings.ambientMode;
            _streetlightRoadOldAmbientLight = RenderSettings.ambientLight;
            _streetlightRoadOldAmbientIntensity = RenderSettings.ambientIntensity;
            _streetlightRoadOldSkybox = RenderSettings.skybox;
            _streetlightRoadOldFog = RenderSettings.fog;
            _streetlightRoadOldFogColor = RenderSettings.fogColor;
            _streetlightRoadOldFogMode = RenderSettings.fogMode;
            _streetlightRoadOldFogDensity = RenderSettings.fogDensity;
            _streetlightRoadEnvironmentSaved = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            Color backdrop = RoadColor(_roadBackgroundColor.Value, Color.black);
            RenderSettings.fogColor = backdrop;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Clamp(_roadFogDensity.Value, 0f, 0.3f);
        }

        private void EnforceStreetlightRoadLighting()
        {
            if (_streetlightRoadRoot == null) return;
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light == null || light == _streetlightRoadSpot || !light.enabled) continue;
                light.enabled = false;
                if (!_streetlightRoadDisabledLights.Contains(light)) _streetlightRoadDisabledLights.Add(light);
            }
        }

        private void ApplyStreetlightCameraPreview()
        {
            if (!_roadCameraPreview.Value) { RestoreStreetlightCameraPreview(); return; }
            HSceneProc scene = FindObjectOfType<HSceneProc>();
            CameraControl_Ver2 controller = scene != null && scene.flags != null ? scene.flags.ctrlCamera : null;
            if (controller == null || controller.transBase == null || _streetlightRoadRoot == null) return;
            if (_roadPreviewController != controller)
            {
                RestoreStreetlightCameraPreview();
                _roadPreviewController = controller;
                _roadPreviousCameraData = controller.GetCameraData();
            }
            Vector3 origin = _streetlightRoadRoot.transform.position;
            Quaternion roadRotation = _streetlightRoadRoot.transform.rotation;
            Vector3 cameraWorld = origin + roadRotation * new Vector3(0f,
                Mathf.Clamp(_roadCameraHeight.Value, 0.5f, 15f),
                -Mathf.Clamp(_roadCameraDistance.Value, 2f, 40f));
            Vector3 targetWorld = origin + roadRotation * new Vector3(0f,
                Mathf.Clamp(_roadCameraTargetHeight.Value, 0f, 12f), 0f);
            Quaternion worldRotation = Quaternion.LookRotation(targetWorld - cameraWorld, Vector3.up);
            BaseCameraControl_Ver2.CameraData data = controller.GetCameraData();
            data.Pos = controller.transBase.InverseTransformPoint(cameraWorld);
            data.Dir = Vector3.zero;
            data.Rot = (Quaternion.Inverse(controller.transBase.rotation) * worldRotation).eulerAngles;
            data.Fov = Mathf.Clamp(_roadCameraFov.Value, 25f, 90f);
            controller.SetCameraData(data);
        }

        private void RestoreStreetlightCameraPreview()
        {
            if (_roadPreviewController != null) _roadPreviewController.SetCameraData(_roadPreviousCameraData);
            _roadPreviewController = null;
        }

        private void DestroyStreetlightRoad()
        {
            if (_streetlightRoadRoot != null) Destroy(_streetlightRoadRoot);
            _streetlightRoadRoot = null;
            _streetlightRoadBackdrop = null;
            _streetlightRoadSpot = null;
            foreach (Light light in _streetlightRoadDisabledLights)
                if (light != null) light.enabled = true;
            _streetlightRoadDisabledLights.Clear();
            if (_streetlightRoadEnvironmentSaved)
            {
                RenderSettings.ambientMode = _streetlightRoadOldAmbientMode;
                RenderSettings.ambientLight = _streetlightRoadOldAmbientLight;
                RenderSettings.ambientIntensity = _streetlightRoadOldAmbientIntensity;
                RenderSettings.skybox = _streetlightRoadOldSkybox;
                RenderSettings.fog = _streetlightRoadOldFog;
                RenderSettings.fogColor = _streetlightRoadOldFogColor;
                RenderSettings.fogMode = _streetlightRoadOldFogMode;
                RenderSettings.fogDensity = _streetlightRoadOldFogDensity;
                _streetlightRoadEnvironmentSaved = false;
            }
            foreach (Material material in _streetlightRoadMaterials)
                if (material != null) Destroy(material);
            _streetlightRoadMaterials.Clear();
            foreach (Texture2D texture in _streetlightRoadTextures)
                if (texture != null) Destroy(texture);
            _streetlightRoadTextures.Clear();
        }
    }
}
