using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private readonly List<Renderer> _roomOverlayHiddenRenderers = new List<Renderer>();
        private readonly List<Light> _roomOverlayHiddenLights = new List<Light>();
        private readonly List<MapInfo.Param> _roomOverlayChoices = new List<MapInfo.Param>();
        private string _roomOverlaySceneName;
        private string _roomOverlayBundleName;
        private string _roomOverlayDisplayName;
        private string _roomOverlayAbandonedSceneName;
        private string _roomOverlayAbandonedBundleName;
        private bool _roomOverlayLoading;
        private float _roomOverlayLoadStartedAt;
        private bool _roomOverlayDropdownOpen;
        private bool _vamHotelEditOpen;
        private bool _roomOverlayPositionEditOpen;
        private string _roomOverlayPositionKey;
        private GameObject _roomOverlayPositionRoot;
        private Vector3 _roomOverlayBasePosition;
        private Quaternion _roomOverlayBaseRotation;
        private Vector3 _roomOverlayActorAnchor;
        private Rect _roomOverlayButtonRect;
        private Rect _roomOverlayListRect;
        private Vector2 _roomOverlayScroll;

        private bool CanUseRoomOverlay()
        {
            BaseMap map = LastReservedMap;
            return _settings != null && map != null && map.mapRoot != null &&
                map.no == _settings.AddedMapNo && FindObjectOfType<HSceneProc>() != null;
        }

        private void RefreshRoomOverlayChoices()
        {
            _roomOverlayChoices.Clear();
            BaseMap map = LastReservedMap;
            if (!CanUseRoomOverlay() || map.infoDic == null) return;
            _roomOverlayChoices.AddRange(map.infoDic.Values.Where(p => p != null &&
                p.No != _settings.AddedMapNo && !string.IsNullOrEmpty(p.AssetBundleName) &&
                !string.IsNullOrEmpty(p.AssetName))
                .OrderBy(p => p.No));
        }

        private void TickRoomOverlay()
        {
            if (!CanUseRoomOverlay())
            {
                ReleaseMapSkyEnvironment();
                _roomOverlayDropdownOpen = false;
                _vamHotelEditOpen = false;
                _roomOverlayPositionEditOpen = false;
                if (_roomOverlayLoading || !string.IsNullOrEmpty(_roomOverlaySceneName)) ClearRoomOverlay();
                return;
            }
            if (_roomOverlayLoading && Time.realtimeSinceStartup - _roomOverlayLoadStartedAt > 20f)
            {
                LogWarn("room overlay load timed out: " + _roomOverlaySceneName);
                ClearRoomOverlay();
            }
            if (_roomOverlayLoading) return;
            TickMapSky();
            if (string.IsNullOrEmpty(_roomOverlaySceneName)) return;
            if (_roomOverlaySceneName == VamHotelSceneName) ApplyVamHotelTransform();
            else ApplyRoomOverlayPosition();
            // The media room may recreate its meshes or lights while the overlay is visible.
            HideSourceRoomForOverlay();
            if (_roomOverlaySceneName == StreetlightRoadSceneKey)
            {
                RebuildStreetlightRoadIfNeeded();
                EnforceStreetlightRoadLighting();
            }
        }

        private void HideSourceRoomForOverlay()
        {
            GameObject source = LastReservedMap != null ? LastReservedMap.mapRoot : null;
            if (source == null) return;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && renderer.enabled) { renderer.enabled = false; _roomOverlayHiddenRenderers.Add(renderer); }
            foreach (Light light in source.GetComponentsInChildren<Light>(true))
                if (light != null && light.enabled) { light.enabled = false; _roomOverlayHiddenLights.Add(light); }
        }

        private void RestoreSourceRoomAfterOverlay()
        {
            foreach (Renderer renderer in _roomOverlayHiddenRenderers)
                if (renderer != null) renderer.enabled = true;
            foreach (Light light in _roomOverlayHiddenLights)
                if (light != null) light.enabled = true;
            _roomOverlayHiddenRenderers.Clear();
            _roomOverlayHiddenLights.Clear();
        }

        private void SelectRoomOverlay(MapInfo.Param choice)
        {
            if (!CanUseRoomOverlay() || _roomOverlayLoading || choice == null) return;
            _roomOverlayDropdownOpen = false;
            StartCoroutine(SwitchRoomOverlay(choice));
        }

        private IEnumerator SwitchRoomOverlay(MapInfo.Param choice)
        {
            _roomOverlayLoading = true;
            _roomOverlayLoadStartedAt = Time.realtimeSinceStartup;
            yield return UnloadRoomOverlay();
            if (!CanUseRoomOverlay()) { _roomOverlayLoading = false; yield break; }
            _roomOverlaySceneName = choice.AssetName;
            _roomOverlayBundleName = choice.AssetBundleName;
            _roomOverlayDisplayName = choice.DisplayName;
            _roomOverlayPositionKey = choice.No.ToString(System.Globalization.CultureInfo.InvariantCulture);
            EnsureRoomOverlayPositionConfig(_roomOverlayPositionKey);
            try
            {
                LogInfo($"room overlay load no={choice.No} scene={choice.AssetName} bundle={choice.AssetBundleName}");
                AssetBundleManager.LoadLevel(choice.AssetBundleName, choice.AssetName, true);
            }
            catch (Exception ex)
            {
                LogWarn("room overlay load failed: " + ex);
                _roomOverlaySceneName = null;
                _roomOverlayBundleName = null;
                _roomOverlayDisplayName = null;
                _roomOverlayLoading = false;
            }
        }

        private void OnRoomOverlaySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.IsNullOrEmpty(_roomOverlayAbandonedSceneName) && scene.name == _roomOverlayAbandonedSceneName)
            {
                SceneManager.UnloadSceneAsync(scene);
                if (IsVamHotelScene(scene.name)) UnloadVamHotelBundle();
                if (!string.IsNullOrEmpty(_roomOverlayAbandonedBundleName))
                    AssetBundleManager.UnloadAssetBundle(_roomOverlayAbandonedBundleName, false);
                _roomOverlayAbandonedSceneName = null;
                _roomOverlayAbandonedBundleName = null;
                return;
            }
            if (string.IsNullOrEmpty(_roomOverlaySceneName) || scene.name != _roomOverlaySceneName) return;
            if (!CanUseRoomOverlay()) { ClearRoomOverlay(); return; }
            if (IsVamHotelScene(scene.name)) { OnVamHotelSceneLoaded(scene); return; }
            try
            {
                GameObject target = scene.GetRootGameObjects().FirstOrDefault(x => x != null && x.name == BaseMap.MAP_ROOT_NAME);
                ChaControl actor = FindObjectsOfType<ChaControl>().FirstOrDefault(x => x != null && x.sex == 1);
                Renderer[] renderers = target != null ? target.GetComponentsInChildren<Renderer>(true).Where(x => x != null && x.enabled).ToArray() : new Renderer[0];
                if (target == null || actor == null || renderers.Length == 0)
                    throw new InvalidOperationException("map root, H actor, or visible geometry missing");
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                target.transform.position += new Vector3(actor.transform.position.x - bounds.center.x,
                    actor.transform.position.y - bounds.min.y, actor.transform.position.z - bounds.center.z);
                _roomOverlayPositionRoot = target;
                _roomOverlayBasePosition = target.transform.position;
                _roomOverlayBaseRotation = target.transform.rotation;
                _roomOverlayActorAnchor = actor.transform.position;
                ApplyRoomOverlayPosition();
                HideSourceRoomForOverlay();
                _roomOverlayLoading = false;
                LogInfo($"room overlay shown scene={scene.name} renderers={renderers.Length} sourceLightsOff={_roomOverlayHiddenLights.Count}");
            }
            catch (Exception ex)
            {
                LogWarn("room overlay display failed: " + ex);
                ClearRoomOverlay();
            }
        }

        private IEnumerator UnloadRoomOverlay()
        {
            ReleaseMapSkyEnvironment();
            string oldScene = _roomOverlaySceneName;
            string oldBundle = _roomOverlayBundleName;
            _roomOverlaySceneName = null;
            _roomOverlayBundleName = null;
            _roomOverlayDisplayName = null;
            _roomOverlayPositionRoot = null;
            _roomOverlayPositionKey = null;
            _roomOverlayPositionEditOpen = false;
            RestoreSourceRoomAfterOverlay();
            if (oldScene == StreetlightRoadSceneKey) { RestoreStreetlightCameraPreview(); DestroyStreetlightRoad(); }
            if (!string.IsNullOrEmpty(oldScene) && oldScene != StreetlightRoadSceneKey)
            {
                Scene scene = SceneManager.GetSceneByName(oldScene);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                    if (operation != null) yield return operation;
                }
            }
            if (!string.IsNullOrEmpty(oldBundle)) AssetBundleManager.UnloadAssetBundle(oldBundle, false);
            if (IsVamHotelScene(oldScene)) UnloadVamHotelBundle();
        }

        private void ClearRoomOverlay()
        {
            _roomOverlayDropdownOpen = false;
            _roomOverlayPositionEditOpen = false;
            if (string.IsNullOrEmpty(_roomOverlaySceneName)) { RestoreSourceRoomAfterOverlay(); return; }
            if (_roomOverlayLoading)
            {
                Scene loadingScene = SceneManager.GetSceneByName(_roomOverlaySceneName);
                if (!loadingScene.IsValid() || !loadingScene.isLoaded)
                {
                    _roomOverlayAbandonedSceneName = _roomOverlaySceneName;
                    _roomOverlayAbandonedBundleName = _roomOverlayBundleName;
                    _roomOverlaySceneName = null;
                    _roomOverlayBundleName = null;
                    _roomOverlayDisplayName = null;
                    _roomOverlayPositionRoot = null;
                    _roomOverlayPositionKey = null;
                    _roomOverlayLoading = false;
                    RestoreSourceRoomAfterOverlay();
                    return;
                }
            }
            StartCoroutine(UnloadRoomOverlay());
            _roomOverlayLoading = false;
        }

        private void DisposeRoomOverlay()
        {
            ReleaseMapSkyEnvironment();
            _roomOverlayDropdownOpen = false;
            RestoreStreetlightCameraPreview();
            RestoreSourceRoomAfterOverlay();
            DestroyStreetlightRoad();
            if (!string.IsNullOrEmpty(_roomOverlaySceneName) && _roomOverlaySceneName != StreetlightRoadSceneKey)
            {
                Scene scene = SceneManager.GetSceneByName(_roomOverlaySceneName);
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
                if (!string.IsNullOrEmpty(_roomOverlayBundleName))
                    AssetBundleManager.UnloadAssetBundle(_roomOverlayBundleName, false);
            }
            UnloadVamHotelBundle();
            _roomOverlaySceneName = null;
            _roomOverlayBundleName = null;
            _roomOverlayDisplayName = null;
            _roomOverlayPositionRoot = null;
            _roomOverlayPositionKey = null;
            _roomOverlayPositionEditOpen = false;
            _roomOverlayLoading = false;
        }

        private void DrawRoomOverlayButton(Rect rect)
        {
            _roomOverlayButtonRect = rect;
            bool available = CanUseRoomOverlay();
            bool previous = GUI.enabled;
            GUI.enabled = previous && available && !_roomOverlayLoading;
            string label = _roomOverlayLoading ? "部屋 読込中" :
                string.IsNullOrEmpty(_roomOverlayDisplayName) ? "部屋: 元の部屋 ▼" : "部屋: " + _roomOverlayDisplayName + " ▼";
            if (GUI.Button(rect, new GUIContent(label, "オールポーズマップ内に別の部屋を呼び出す")))
            {
                _roomOverlayDropdownOpen = !_roomOverlayDropdownOpen;
                _vamHotelEditOpen = false;
                _roomOverlayPositionEditOpen = false;
                if (_roomOverlayDropdownOpen)
                {
                    _roomOverlayScroll = Vector2.zero;
                    RefreshRoomOverlayChoices();
                }
            }
            GUI.enabled = previous;
            if (!available) _roomOverlayDropdownOpen = false;
        }

        private void DrawRoomOverlayPopup()
        {
            _roomOverlayListRect = Rect.zero;
            if (!_roomOverlayDropdownOpen) return;
            if (_vamHotelEditOpen) { DrawVamHotelSettingsPopup(); return; }
            if (_roomOverlayPositionEditOpen) { DrawRoomOverlayPositionPopup(); return; }
            const float rowHeight = 22f;
            bool hotelActive = _roomOverlaySceneName == VamHotelSceneName;
            bool positionActive = !string.IsNullOrEmpty(_roomOverlaySceneName) && !hotelActive;
            float headerHeight = hotelActive || positionActive ? rowHeight + 3f : 0f;
            float height = Mathf.Min(330f, (_roomOverlayChoices.Count + 3) * rowHeight + 6f + headerHeight);
            float y = Mathf.Max(2f, _roomOverlayButtonRect.y - height - 3f);
            _roomOverlayListRect = new Rect(_roomOverlayButtonRect.x, y, Mathf.Max(220f, _roomOverlayButtonRect.width), height);
            GUI.Box(_roomOverlayListRect, GUIContent.none);
            if (hotelActive && GUI.Button(new Rect(_roomOverlayListRect.x + 3f, y + 3f,
                _roomOverlayListRect.width - 6f, rowHeight), "ホテルの位置・向きを調整"))
            {
                ResetVamHotelInputs();
                _vamHotelEditOpen = true;
            }
            else if (positionActive && GUI.Button(new Rect(_roomOverlayListRect.x + 3f, y + 3f,
                _roomOverlayListRect.width - 6f, rowHeight), "このマップの位置を調整"))
            {
                ResetRoomOverlayPositionInputs();
                _roomOverlayPositionEditOpen = true;
            }
            Rect viewport = new Rect(_roomOverlayListRect.x + 3f, y + 3f + headerHeight,
                _roomOverlayListRect.width - 6f, height - 6f - headerHeight);
            Rect content = new Rect(0, 0, viewport.width - 18f, (_roomOverlayChoices.Count + 3) * rowHeight);
            _roomOverlayScroll = GUI.BeginScrollView(viewport, _roomOverlayScroll, content);
            if (GUI.Button(new Rect(0, 0, content.width, rowHeight - 1f), "元の部屋に戻す"))
            {
                _roomOverlayDropdownOpen = false;
                ClearRoomOverlay();
            }
            if (GUI.Button(new Rect(0, rowHeight, content.width, rowHeight - 1f), "街灯のある暗い道路"))
                SelectStreetlightRoadOverlay();
            if (GUI.Button(new Rect(0, rowHeight * 2, content.width, rowHeight - 1f), "VaM ホテル（照明あり）"))
                SelectVamHotelOverlay();
            for (int i = 0; i < _roomOverlayChoices.Count; i++)
            {
                MapInfo.Param choice = _roomOverlayChoices[i];
                string name = string.IsNullOrEmpty(choice.DisplayName) ? choice.MapName : choice.DisplayName;
                if (GUI.Button(new Rect(0, (i + 3) * rowHeight, content.width, rowHeight - 1f), name)) SelectRoomOverlay(choice);
            }
            GUI.EndScrollView();
            if (Event.current != null && Event.current.type == EventType.MouseDown &&
                !_roomOverlayListRect.Contains(Event.current.mousePosition) && !_roomOverlayButtonRect.Contains(Event.current.mousePosition))
                _roomOverlayDropdownOpen = false;
        }
    }
}
