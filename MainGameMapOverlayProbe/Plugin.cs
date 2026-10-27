using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MainGameMapOverlayProbe
{
    [BepInPlugin("com.kks.maingamemapoverlayprobe", "MainGameMapOverlayProbe", "0.1.0")]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string TargetMapName = "hotelmyroom";
        private string _loadedSceneName;
        private string _loadedBundleName;
        private readonly List<Renderer> _hiddenOriginalRenderers = new List<Renderer>();
        private readonly List<Light> _originalLights = new List<Light>();
        private bool _originalLightsOff;

        private void Awake()
        {
            Logger.LogInfo("probe ready: F10 loads hotelmyroom additively in H; F11 unloads it");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (Bindings.Down("MainGameMapOverlayProbe.load", "F10")) LoadTarget();
            if (Bindings.Down("MainGameMapOverlayProbe.unload", "F11")) UnloadTarget();
            if (Bindings.Down("MainGameMapOverlayProbe.lights", "F12")) ToggleOriginalLights();
        }

        private void LoadTarget()
        {
            try
            {
                var h = FindObjectOfType<HSceneProc>();
                var map = FindObjectOfType<BaseMap>();
                Logger.LogInfo($"before load: h={(h != null)} map={(map != null ? map.no.ToString() : "null")} root={(map != null && map.mapRoot != null ? map.mapRoot.name : "null")} active={SceneManager.GetActiveScene().name}");
                if (h == null || map == null) return;
                if (!string.IsNullOrEmpty(_loadedSceneName)) { Logger.LogWarning("target already loaded"); return; }
                var p = map.infoDic?.Values.FirstOrDefault(x => string.Equals(x.MapName, TargetMapName, StringComparison.OrdinalIgnoreCase)
                    || (x.AssetBundleName != null && x.AssetBundleName.IndexOf(TargetMapName, StringComparison.OrdinalIgnoreCase) >= 0)
                    || string.Equals(x.AssetName, TargetMapName, StringComparison.OrdinalIgnoreCase));
                if (p == null)
                {
                    Logger.LogWarning("target map not in catalog: " + TargetMapName);
                    foreach (var candidate in map.infoDic.Values.Where(x => x.AssetBundleName != null && x.AssetBundleName.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0).Take(30))
                        Logger.LogInfo($"room candidate: no={candidate.No} name={candidate.MapName} bundle={candidate.AssetBundleName} scene={candidate.AssetName}");
                    return;
                }
                Logger.LogInfo($"load request: no={p.No} bundle={p.AssetBundleName} scene={p.AssetName}");
                _loadedSceneName = p.AssetName;
                _loadedBundleName = p.AssetBundleName;
                AssetBundleManager.LoadLevel(p.AssetBundleName, p.AssetName, true);
            }
            catch (Exception ex) { Logger.LogError("load exception: " + ex); _loadedSceneName = null; _loadedBundleName = null; }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != _loadedSceneName) return;
            var roots = scene.GetRootGameObjects();
            Logger.LogInfo($"loaded: scene={scene.name} mode={mode} roots={roots.Length} mapRoot={roots.Any(x => x != null && x.name == BaseMap.MAP_ROOT_NAME)} active={SceneManager.GetActiveScene().name} h={(FindObjectOfType<HSceneProc>() != null)}");
            var target = roots.FirstOrDefault(x => x != null && x.name == BaseMap.MAP_ROOT_NAME);
            var original = FindObjectOfType<BaseMap>()?.mapRoot;
            var actor = FindObjectsOfType<ChaControl>().FirstOrDefault(x => x != null && x.sex == 1);
            if (target == null || original == null || actor == null) return;
            var renderers = target.GetComponentsInChildren<Renderer>(true).Where(x => x != null && x.enabled).ToArray();
            if (renderers.Length == 0) { Logger.LogWarning("target has no active renderers"); return; }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Logger.LogInfo($"target geometry: renderers={renderers.Length} boundsCenter={bounds.center} boundsSize={bounds.size} actor={actor.transform.position} original={original.name}");
            target.transform.position += new Vector3(
                actor.transform.position.x - bounds.center.x,
                actor.transform.position.y - bounds.min.y,
                actor.transform.position.z - bounds.center.z);
            foreach (var renderer in original.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                _hiddenOriginalRenderers.Add(renderer);
            }
            Logger.LogInfo($"aligned: targetPosition={target.transform.position} hiddenOriginalRenderers={_hiddenOriginalRenderers.Count} h={(FindObjectOfType<HSceneProc>() != null)}");
            _originalLights.Clear();
            _originalLights.AddRange(original.GetComponentsInChildren<Light>(true).Where(x => x != null && x.enabled));
            Logger.LogInfo($"lighting baseline: originalEnabled={_originalLights.Count} targetEnabled={target.GetComponentsInChildren<Light>(true).Count(x => x != null && x.enabled)} originalLightsOff={_originalLightsOff}");
        }

        private void ToggleOriginalLights()
        {
            if (string.IsNullOrEmpty(_loadedSceneName)) return;
            _originalLightsOff = !_originalLightsOff;
            foreach (var light in _originalLights) if (light != null) light.enabled = !_originalLightsOff;
            Logger.LogInfo($"original lights {(_originalLightsOff ? "OFF" : "ON")}: count={_originalLights.Count} h={(FindObjectOfType<HSceneProc>() != null)} active={SceneManager.GetActiveScene().name}");
        }

        private void UnloadTarget()
        {
            try
            {
                if (string.IsNullOrEmpty(_loadedSceneName)) return;
                var scene = SceneManager.GetSceneByName(_loadedSceneName);
                Logger.LogInfo($"unload request: scene={_loadedSceneName} valid={scene.IsValid()} loaded={scene.isLoaded}");
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
                foreach (var renderer in _hiddenOriginalRenderers) if (renderer != null) renderer.enabled = true;
                foreach (var light in _originalLights) if (light != null) light.enabled = true;
                Logger.LogInfo($"restored original renderers={_hiddenOriginalRenderers.Count} h={(FindObjectOfType<HSceneProc>() != null)}");
                _hiddenOriginalRenderers.Clear();
                _originalLights.Clear();
                _originalLightsOff = false;
                if (!string.IsNullOrEmpty(_loadedBundleName)) AssetBundleManager.UnloadAssetBundle(_loadedBundleName, false);
                _loadedSceneName = null;
                _loadedBundleName = null;
            }
            catch (Exception ex) { Logger.LogError("unload exception: " + ex); }
        }
    }
}
