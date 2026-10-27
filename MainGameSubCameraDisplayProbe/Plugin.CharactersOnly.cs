using System.Collections.Generic;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private const int CharactersOnlyDedicatedLayer = 31;
        private const float CharactersOnlyCharacterRescanInterval = 0.5f;
        private const float CharactersOnlyRendererRescanInterval = 5f;
        private const float CharactersOnlyStatsLogInterval = 5f;

        private struct LayerSwap
        {
            public GameObject GameObject;
            public int OriginalLayer;
        }

        private struct CharacterRenderer
        {
            public Renderer Renderer;
            public GameObject GameObject;
        }

        private bool _charactersOnlyHooked;
        private Light[] _charactersOnlyLights = new Light[0];
        private float _charactersOnlyLightNextRescan;
        private readonly Dictionary<Light, int> _charactersOnlyLightMasks = new Dictionary<Light, int>();
        private float _charactersOnlyCharacterNextRescan;
        private readonly List<CharacterRenderer> _charactersOnlyKeepObjects = new List<CharacterRenderer>(512);
        private readonly Dictionary<ChaControl, Renderer[]> _charactersOnlyRendererCache = new Dictionary<ChaControl, Renderer[]>();
        private float _charactersOnlyRendererNextRescan;
        private readonly List<LayerSwap> _charactersOnlySwappedThisFrame = new List<LayerSwap>(512);
        private bool _charactersOnlyApplying;
        private int _charactersOnlySavedCullingMask;
        private int _charactersOnlyPreCullCount;
        private int _charactersOnlyPostRenderCount;
        private int _charactersOnlyLastChaControlCount;
        private float _charactersOnlyNextStatsLog;

        private void EnableCharactersOnlyHook()
        {
            if (_charactersOnlyHooked)
                return;

            Camera.onPreCull += OnCharactersOnlyPreCull;
            Camera.onPostRender += OnCharactersOnlyPostRender;
            _charactersOnlyHooked = true;
            _charactersOnlyPreCullCount = 0;
            _charactersOnlyPostRenderCount = 0;
            _charactersOnlyNextStatsLog = Time.unscaledTime + CharactersOnlyStatsLogInterval;
            LogInfo("[characters-only] hook enabled subCamera=" + (_subCamera != null ? _subCamera.name : "null") + " dedicatedLayer=" + CharactersOnlyDedicatedLayer);
        }

        private void DisableCharactersOnlyHook()
        {
            RestoreExternalRenderScope();
            if (!_charactersOnlyHooked)
                return;

            Camera.onPreCull -= OnCharactersOnlyPreCull;
            Camera.onPostRender -= OnCharactersOnlyPostRender;
            _charactersOnlyHooked = false;

            RestoreCharactersOnlySwapped();
            _charactersOnlyApplying = false;
            _charactersOnlyKeepObjects.Clear();
            _charactersOnlyRendererCache.Clear();
            _charactersOnlyCharacterNextRescan = 0f;
            _charactersOnlyRendererNextRescan = 0f;
            LogInfo("[characters-only] hook disabled");
        }

        private void OnCharactersOnlyPreCull(Camera cam)
        {
            if (_charactersOnlyApplying) RestoreExternalRenderScope();
            if (cam == null || cam != _subCamera)
                return;
            if (_settings == null || !_settings.CharactersOnlyMode)
                return;
            if (_charactersOnlyApplying)
            {
                LogWarn("[characters-only] PreCull re-entry detected cam=" + cam.name + " — skipping");
                return;
            }

            float now = Time.unscaledTime;
            if (now >= _charactersOnlyCharacterNextRescan || _charactersOnlyKeepObjects.Count == 0)
            {
                int before = _charactersOnlyKeepObjects.Count;
                RebuildCharactersOnlyKeepObjects();
                _charactersOnlyCharacterNextRescan = now + CharactersOnlyCharacterRescanInterval;
                if (_runtimeLoggingEnabled && _charactersOnlyKeepObjects.Count != before)
                    LogInfo("[characters-only] keep set rebuilt chaCount=" + _charactersOnlyLastChaControlCount + " renderers=" + _charactersOnlyKeepObjects.Count);
            }

            _charactersOnlySwappedThisFrame.Clear();
            int dedicatedLayer = CharactersOnlyDedicatedLayer;
            int sourceLayers = 0;
            for (int i = 0; i < _charactersOnlyKeepObjects.Count; i++)
            {
                CharacterRenderer keep = _charactersOnlyKeepObjects[i];
                Renderer renderer = keep.Renderer;
                if (renderer == null || !renderer.enabled)
                    continue;
                GameObject go = keep.GameObject;
                if (go == null || !go.activeInHierarchy)
                    continue;

                int original = go.layer;
                sourceLayers |= 1 << original;
                if (original == dedicatedLayer)
                    continue;

                _charactersOnlySwappedThisFrame.Add(new LayerSwap { GameObject = go, OriginalLayer = original });
                go.layer = dedicatedLayer;
            }

            _renderScopeCamera = _subCamera;
            _charactersOnlySavedCullingMask = cam.cullingMask;
            cam.cullingMask = 1 << dedicatedLayer;
            _charactersOnlyApplying = true;
            // Preserve the existing room lighting across the temporary layer change.
            // Only inspect the light list periodically, never scan the scene per frame.
            if (now >= _charactersOnlyLightNextRescan) {
                _charactersOnlyLights = Object.FindObjectsOfType<Light>();
                _charactersOnlyLightNextRescan = now + CharactersOnlyCharacterRescanInterval;
            }
            foreach (var light in _charactersOnlyLights) {
                if (light == null || !light.isActiveAndEnabled) continue;
                int mask = light.cullingMask;
                if ((mask & sourceLayers) == 0 || (mask & (1 << dedicatedLayer)) != 0) continue;
                _charactersOnlyLightMasks[light] = mask;
                light.cullingMask = mask | (1 << dedicatedLayer);
            }
            _charactersOnlyPreCullCount++;

            if (now >= _charactersOnlyNextStatsLog)
            {
                if (_runtimeLoggingEnabled)
                    LogInfo("[characters-only] stats interval=" + CharactersOnlyStatsLogInterval + "s preCullCount=" + _charactersOnlyPreCullCount
                        + " postRenderCount=" + _charactersOnlyPostRenderCount
                        + " keepObjects=" + _charactersOnlyKeepObjects.Count
                        + " swappedThisCall=" + _charactersOnlySwappedThisFrame.Count
                        + " savedMask=0x" + _charactersOnlySavedCullingMask.ToString("X8")
                        + " newMask=0x" + cam.cullingMask.ToString("X8"));
                _charactersOnlyPreCullCount = 0;
                _charactersOnlyPostRenderCount = 0;
                _charactersOnlyNextStatsLog = now + CharactersOnlyStatsLogInterval;
            }
        }

        private void OnCharactersOnlyPostRender(Camera cam)
        {
            if (cam == null || cam != _renderScopeCamera)
                return;
            if (!_charactersOnlyApplying)
                return;

            cam.cullingMask = _charactersOnlySavedCullingMask;
            RestoreCharactersOnlySwapped();
            _charactersOnlyApplying = false;
            _charactersOnlyPostRenderCount++;
        }

        private void RestoreCharactersOnlySwapped()
        {
            foreach (var pair in _charactersOnlyLightMasks)
                if (pair.Key != null) pair.Key.cullingMask = pair.Value;
            _charactersOnlyLightMasks.Clear();
            for (int i = 0; i < _charactersOnlySwappedThisFrame.Count; i++)
            {
                LayerSwap swap = _charactersOnlySwappedThisFrame[i];
                if (swap.GameObject != null)
                    swap.GameObject.layer = swap.OriginalLayer;
            }
            _charactersOnlySwappedThisFrame.Clear();
        }

        private void RebuildCharactersOnlyKeepObjects()
        {
            _charactersOnlyKeepObjects.Clear();
            if (Time.unscaledTime >= _charactersOnlyRendererNextRescan)
            {
                _charactersOnlyRendererCache.Clear();
                _charactersOnlyRendererNextRescan = Time.unscaledTime + CharactersOnlyRendererRescanInterval;
            }
            // Camera ownership selects the follow target, not which characters exist
            // in the image. Keep H participants and independent actors renderable.
            ChaControl[] characters = Object.FindObjectsOfType<ChaControl>();
            _charactersOnlyLastChaControlCount = characters.Length;
            for (int i = 0; i < characters.Length; i++)
            {
                ChaControl cha = characters[i];
                if (cha == null)
                    continue;

                Renderer[] charRenderers;
                if (!_charactersOnlyRendererCache.TryGetValue(cha, out charRenderers))
                {
                    charRenderers = cha.GetComponentsInChildren<Renderer>(true);
                    _charactersOnlyRendererCache[cha] = charRenderers;
                }
                for (int j = 0; j < charRenderers.Length; j++)
                {
                    Renderer r = charRenderers[j];
                    if (r == null)
                        continue;
                    _charactersOnlyKeepObjects.Add(new CharacterRenderer
                    {
                        Renderer = r,
                        GameObject = r.gameObject
                    });
                }
            }
        }

        private void SyncCharactersOnlyHook()
        {
            if (_settings == null)
                return;

            if (_charactersOnlyApplying) RestoreExternalRenderScope();

            bool shouldHook = _settings.CharactersOnlyMode && _settings.Enabled && _subCamera != null;
            if (shouldHook)
                EnableCharactersOnlyHook();
            else
                DisableCharactersOnlyHook();

            if (_subCamera != null)
            {
                Color desiredBg = (_settings.CharactersOnlyMode && _settings.Enabled) ? Color.clear : Color.black;
                if (_subCamera.backgroundColor != desiredBg)
                    _subCamera.backgroundColor = desiredBg;
            }
        }
    }
}
