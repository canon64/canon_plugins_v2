using System;
using UnityEngine;

namespace MainGameBlankMapAdd.Map
{
    internal sealed class SourceMapBlankifier
    {
        private readonly Action<string> _info;
        private int _lastRootId = int.MinValue;

        internal SourceMapBlankifier(Action<string> info)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
        }

        internal void Reset()
        {
            _lastRootId = int.MinValue;
        }

        internal void Apply(BaseMap map, PluginSettings settings)
        {
            if (map?.mapRoot == null || settings == null || !settings.BlankifySceneOnLoad) return;
            int rootId = map.mapRoot.GetInstanceID();
            if (_lastRootId == rootId) return;
            _lastRootId = rootId;

            int rendererCount = DisableRenderers(map.mapRoot, settings.DisableRenderers);
            int terrainCount = DisableTerrains(map.mapRoot, settings.DisableTerrains);
            CountPair lights = ConfigureLights(map.mapRoot, settings.DisableLights);
            int particleCount = DisableParticles(map.mapRoot, settings.DisableParticles);
            int audioCount = MuteAudio(map.mapRoot, settings.DisableAudioSources);

            _info($"blankify no={map.no} root={map.mapRoot.name} renderers={rendererCount} terrains={terrainCount} lightsOff={lights.Disabled} lightsRestore={lights.Restored} particles={particleCount} audio={audioCount}");
        }

        private static int DisableRenderers(GameObject root, bool enabled)
        {
            if (!enabled) return 0;
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                count++;
            }
            return count;
        }

        private static int DisableTerrains(GameObject root, bool enabled)
        {
            if (!enabled) return 0;
            int count = 0;
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                if (terrain == null) continue;
                terrain.drawHeightmap = false;
                terrain.drawTreesAndFoliage = false;
                count++;
            }
            return count;
        }

        private static CountPair ConfigureLights(GameObject root, bool disable)
        {
            var result = new CountPair();
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                if (disable && light.enabled) { light.enabled = false; result.Disabled++; }
                else if (!disable && !light.enabled) { light.enabled = true; result.Restored++; }
            }
            return result;
        }

        private static int DisableParticles(GameObject root, bool enabled)
        {
            if (!enabled) return 0;
            int count = 0;
            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particle == null) continue;
                var emission = particle.emission;
                if (!emission.enabled) continue;
                emission.enabled = false;
                count++;
            }
            return count;
        }

        private static int MuteAudio(GameObject root, bool enabled)
        {
            if (!enabled) return 0;
            int count = 0;
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (source == null || source.mute) continue;
                source.mute = true;
                count++;
            }
            return count;
        }

        private sealed class CountPair
        {
            internal int Disabled;
            internal int Restored;
        }
    }
}
