using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private const string VamHotelSceneName = "thehotelroom_lit";
        private AssetBundle _vamHotelBundle;
        private readonly List<Mesh> _vamHotelMovableMeshes = new List<Mesh>();
        private GameObject _vamHotelPivot;
        private Vector3 _vamHotelActorAnchor;
        private ConfigEntry<float> _vamHotelOffsetX;
        private ConfigEntry<float> _vamHotelOffsetY;
        private ConfigEntry<float> _vamHotelOffsetZ;
        private ConfigEntry<float> _vamHotelRotationY;
        private ConfigEntry<float> _vamHotelRotationX;
        private ConfigEntry<float> _vamHotelRotationZ;
        private ConfigEntry<float> _vamHotelScale;
        private readonly string[] _vamHotelInputs = new string[5];

        private void ResetVamHotelInputs()
        {
            ConfigEntry<float>[] entries = { _vamHotelOffsetX, _vamHotelOffsetY,
                _vamHotelOffsetZ, _vamHotelRotationY, _vamHotelScale };
            for (int i = 0; i < entries.Length; i++)
                _vamHotelInputs[i] = entries[i].Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void InitializeVamHotelConfig()
        {
            const string section = "10.VaMHotelMap";
            _vamHotelOffsetX = Config.Bind(section, "PositionX", 0f, "キャラから見たホテルの左右位置。Hシーン中に即時反映");
            _vamHotelOffsetY = Config.Bind(section, "PositionY", 0f, "キャラから見たホテルの上下位置。Hシーン中に即時反映");
            _vamHotelOffsetZ = Config.Bind(section, "PositionZ", 0f, "キャラから見たホテルの前後位置。Hシーン中に即時反映");
            _vamHotelRotationY = Config.Bind(section, "RotationY", 0f, "ホテルの水平回転角度。Hシーン中に即時反映");
            _vamHotelRotationX = Config.Bind(section, "RotationX", 0f, "ホテルのX軸回転角度。Hシーン中に即時反映");
            _vamHotelRotationZ = Config.Bind(section, "RotationZ", 0f, "ホテルのZ軸回転角度。Hシーン中に即時反映");
            _vamHotelScale = Config.Bind(section, "Scale", 1f, "ホテルの大きさ。Hシーン中に即時反映");
        }

        private void DrawVamHotelSettingsPopup()
        {
            const float width = 330f;
            const float height = 220f;
            _roomOverlayListRect = new Rect(_roomOverlayButtonRect.x,
                Mathf.Max(2f, _roomOverlayButtonRect.y - height - 3f), width, height);
            GUI.Box(_roomOverlayListRect, "ホテル位置調整（変更は即時反映）");
            float x = _roomOverlayListRect.x + 9f;
            float y = _roomOverlayListRect.y + 27f;
            DrawVamHotelValueRow(x, y, "左右 X", 0, _vamHotelOffsetX, -1000f, 1000f);
            DrawVamHotelValueRow(x, y + 30f, "上下 Y", 1, _vamHotelOffsetY, -1000f, 1000f);
            DrawVamHotelValueRow(x, y + 60f, "前後 Z", 2, _vamHotelOffsetZ, -1000f, 1000f);
            DrawVamHotelValueRow(x, y + 90f, "向き Y", 3, _vamHotelRotationY, -360f, 360f);
            DrawVamHotelValueRow(x, y + 120f, "倍率", 4, _vamHotelScale, 0.05f, 20f);
            if (GUI.Button(new Rect(x, y + 155f, 145f, 25f), "位置を初期値へ戻す"))
            {
                _vamHotelOffsetX.Value = 0f;
                _vamHotelOffsetY.Value = 0f;
                _vamHotelOffsetZ.Value = 0f;
                _vamHotelRotationY.Value = 0f;
                _vamHotelScale.Value = 1f;
                ResetVamHotelInputs();
            }
            if (GUI.Button(new Rect(x + 194f, y + 155f, 115f, 25f), "閉じる"))
            {
                _vamHotelEditOpen = false;
                _roomOverlayDropdownOpen = false;
            }
        }

        private void DrawVamHotelValueRow(float x, float y, string label, int index,
            ConfigEntry<float> entry, float min, float max)
        {
            GUI.Label(new Rect(x, y, 95f, 24f), label);
            string value = GUI.TextField(new Rect(x + 105f, y, 180f, 24f),
                _vamHotelInputs[index] ?? string.Empty);
            _vamHotelInputs[index] = value;
            float parsed;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                if (!float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    entry.Value = Mathf.Clamp(parsed, min, max);
            }
        }

        private void ApplyVamHotelTransform()
        {
            if (_vamHotelPivot == null || _roomOverlaySceneName != VamHotelSceneName) return;
            Transform pivot = _vamHotelPivot.transform;
            pivot.position = _vamHotelActorAnchor + new Vector3(_vamHotelOffsetX.Value,
                _vamHotelOffsetY.Value, _vamHotelOffsetZ.Value);
            pivot.rotation = Quaternion.Euler(_vamHotelRotationX.Value, _vamHotelRotationY.Value,
                _vamHotelRotationZ.Value);
            pivot.localScale = Vector3.one * Mathf.Clamp(_vamHotelScale.Value, 0.05f, 20f);
        }

        private static bool IsVamHotelScene(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("thehotelroom_", StringComparison.Ordinal);
        }

        private void SelectVamHotelOverlay()
        {
            if (!CanUseRoomOverlay() || _roomOverlayLoading) return;
            _roomOverlayDropdownOpen = false;
            StartCoroutine(SwitchToVamHotelOverlay());
        }

        private IEnumerator SwitchToVamHotelOverlay()
        {
            _roomOverlayLoading = true;
            _roomOverlayLoadStartedAt = Time.realtimeSinceStartup;
            yield return UnloadRoomOverlay();
            if (!CanUseRoomOverlay()) { _roomOverlayLoading = false; yield break; }
            string file = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location),
                "Assets", "thehotelroom_readable.assetbundle");
            try
            {
                if (!File.Exists(file)) throw new FileNotFoundException("VaM hotel asset bundle missing", file);
                _vamHotelBundle = AssetBundle.LoadFromFile(file);
                if (_vamHotelBundle == null) throw new InvalidOperationException("VaM hotel asset bundle could not be loaded");
                string scenePath = _vamHotelBundle.GetAllScenePaths().FirstOrDefault(path =>
                    path.EndsWith("/" + VamHotelSceneName + ".unity", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(scenePath)) throw new InvalidOperationException("lit hotel scene missing in bundle");
                _roomOverlaySceneName = VamHotelSceneName;
                _roomOverlayDisplayName = "VaM ホテル（照明あり）";
                AsyncOperation load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                if (load == null) throw new InvalidOperationException("VaM hotel scene load did not start");
                LogInfo("VaM hotel scene loading: " + scenePath);
            }
            catch (Exception ex)
            {
                LogWarn("VaM hotel load failed: " + ex);
                _roomOverlaySceneName = null;
                _roomOverlayDisplayName = null;
                _roomOverlayLoading = false;
                UnloadVamHotelBundle();
            }
        }

        private void OnVamHotelSceneLoaded(Scene scene)
        {
            try
            {
                GameObject root = scene.GetRootGameObjects().FirstOrDefault(x => x != null && x.name == "the_hotel_room");
                ChaControl actor = FindObjectsOfType<ChaControl>().FirstOrDefault(x => x != null && x.sex == 1);
                if (root == null || actor == null) throw new InvalidOperationException("hotel root or H actor missing");
                ConvertStaticHotelGeometry(root);
                Renderer floor = root.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(x => x != null && x.enabled &&
                        (x.name == "SM_Floor (movable)" || x.name == "SM_Floor"));
                if (floor == null) throw new InvalidOperationException("hotel room floor missing");
                Bounds bounds = floor.bounds;
                Vector3 anchor = bounds.center;
                Vector3 desired = actor.transform.position;
                root.transform.position += new Vector3(desired.x - anchor.x,
                    desired.y - bounds.min.y, desired.z - anchor.z);
                _vamHotelActorAnchor = desired;
                _vamHotelPivot = new GameObject("VaM hotel position, rotation and scale");
                SceneManager.MoveGameObjectToScene(_vamHotelPivot, scene);
                _vamHotelPivot.transform.position = desired;
                root.transform.SetParent(_vamHotelPivot.transform, true);
                ApplyVamHotelTransform();
                HideSourceRoomForOverlay();
                _roomOverlayLoading = false;
                LogInfo("VaM hotel shown: renderers=" + root.GetComponentsInChildren<Renderer>(true).Length);
            }
            catch (Exception ex)
            {
                LogWarn("VaM hotel display failed: " + ex);
                ClearRoomOverlay();
            }
        }

        private void UnloadVamHotelBundle()
        {
            _vamHotelPivot = null;
            foreach (Mesh mesh in _vamHotelMovableMeshes)
                if (mesh != null) Destroy(mesh);
            _vamHotelMovableMeshes.Clear();
            if (_vamHotelBundle == null) return;
            _vamHotelBundle.Unload(true);
            _vamHotelBundle = null;
        }
    }
}
