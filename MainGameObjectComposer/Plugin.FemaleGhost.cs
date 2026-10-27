using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private bool _femaleGhostEnabled;
        private ChaControl _femaleGhostTarget;
        private readonly Dictionary<Material, Material> _femaleGhostMaterials = new Dictionary<Material, Material>();
        private float _nextFemaleGhostScan;
        private readonly Dictionary<Renderer, Material[]> _femaleGhostOriginals = new Dictionary<Renderer, Material[]>();
        private readonly List<Renderer> _femaleGhostRemove = new List<Renderer>();

        private void TickFemaleGhost()
        {
            ChaControl target = _femaleGhostEnabled ? _runtime.MainFemale : null;
            if (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom || target == null)
            {
                RestoreFemaleGhost();
                return;
            }
            if (target != _femaleGhostTarget)
            {
                RestoreFemaleGhost();
                _femaleGhostTarget = target;
            }
            if (Time.unscaledTime < _nextFemaleGhostScan) return;
            _nextFemaleGhostScan = Time.unscaledTime + 0.5f;
            Shader ghostShader = FindFemaleGhostShader();
            if (ghostShader == null) return;

            _femaleGhostRemove.Clear();
            foreach (var entry in _femaleGhostOriginals)
            {
                if (entry.Key == null || !entry.Key.transform.IsChildOf(target.transform))
                    _femaleGhostRemove.Add(entry.Key);
            }
            foreach (Renderer renderer in _femaleGhostRemove)
                RestoreFemaleGhostRenderer(renderer);

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || IsComposerRenderer(renderer.transform)) continue;
                if (IsEyeRenderer(renderer))
                {
                    RestoreFemaleGhostRenderer(renderer);
                    continue;
                }
                Material[] current = renderer.sharedMaterials;
                if (current == null || current.Length == 0) continue;
                if (_femaleGhostOriginals.TryGetValue(renderer, out Material[] original))
                {
                    bool ours = current.Length == original.Length;
                    for (int i = 0; ours && i < current.Length; i++)
                        ours = current[i] == original[i] ||
                            (original[i] != null && _femaleGhostMaterials.TryGetValue(original[i], out Material ghost)
                            && current[i] == ghost);
                    if (ours) continue;
                    // Character reloads can replace material slots while the overlay is active.
                    Material[] updated = new Material[current.Length];
                    for (int i = 0; i < current.Length; i++)
                    {
                        bool wasGhost = i < original.Length && original[i] != null
                            && _femaleGhostMaterials.TryGetValue(original[i], out Material ghost)
                            && current[i] == ghost;
                        updated[i] = wasGhost ? original[i] : current[i];
                    }
                    _femaleGhostOriginals[renderer] = updated;
                    current = updated;
                }
                else _femaleGhostOriginals.Add(renderer, current);

                Material[] replacement = new Material[current.Length];
                for (int i = 0; i < replacement.Length; i++)
                    replacement[i] = GetFemaleGhostMaterial(current[i], ghostShader);
                renderer.sharedMaterials = replacement;
            }
        }

        private Shader FindFemaleGhostShader()
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Transparent/Diffuse")
                ?? Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (shader == null)
            {
                LogError("女キャラ半透明: 透過シェーダーが見つかりません");
                _femaleGhostEnabled = false;
                return null;
            }
            return shader;
        }

        private Material GetFemaleGhostMaterial(Material original, Shader shader)
        {
            if (original == null) return null;
            if (IsEyeMaterial(original)) return original;
            if (_femaleGhostMaterials.TryGetValue(original, out Material cached) && cached != null) return cached;
            var ghost = new Material(shader) { name = "Composer Ghost " + original.name };
            if (original.HasProperty("_MainTex")) ghost.mainTexture = original.mainTexture;
            Color color = original.HasProperty("_Color") ? original.color : Color.white;
            // Skin, clothes and hair are separate overlapping renderers. 25% per
            // layer compounds into an almost opaque character, so keep each
            // layer faint enough for the motion objects behind her to show.
            color.a = 0.03f;
            ghost.color = color;
            if (shader.name == "Standard")
            {
                ghost.SetFloat("_Mode", 2f);
                ghost.SetOverrideTag("RenderType", "Transparent");
                ghost.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                ghost.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                ghost.SetInt("_ZWrite", 0);
                ghost.DisableKeyword("_ALPHATEST_ON");
                ghost.EnableKeyword("_ALPHABLEND_ON");
                ghost.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            ghost.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _femaleGhostMaterials.Add(original, ghost);
            return ghost;
        }

        private bool IsComposerRenderer(Transform transform)
        {
            foreach (RuntimeObjectRef obj in _runtimeObjects.Values)
                if (obj != null && obj.GameObject != null && transform.IsChildOf(obj.GameObject.transform)) return true;
            return false;
        }

        private static bool IsEyeRenderer(Renderer renderer)
        {
            string name = renderer.name.ToLowerInvariant();
            return name.Contains("eye") || name.Contains("hitomi") || name.Contains("siro")
                || name.Contains("white");
        }

        private static bool IsEyeMaterial(Material material)
        {
            string name = material.name.ToLowerInvariant();
            return name.Contains("eye") || name.Contains("hitomi") || name.Contains("siro")
                || name.Contains("white");
        }

        private void RestoreFemaleGhostRenderer(Renderer renderer)
        {
            if (!_femaleGhostOriginals.TryGetValue(renderer, out Material[] original)) return;
            if (renderer != null)
            {
                Material[] current = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < current.Length && i < original.Length; i++)
                {
                    if (original[i] != null && _femaleGhostMaterials.TryGetValue(original[i], out Material ghost)
                        && current[i] == ghost)
                    {
                        current[i] = original[i];
                        changed = true;
                    }
                }
                if (changed) renderer.sharedMaterials = current;
            }
            _femaleGhostOriginals.Remove(renderer);
        }

        private void RestoreFemaleGhost()
        {
            _femaleGhostRemove.Clear();
            _femaleGhostRemove.AddRange(_femaleGhostOriginals.Keys);
            foreach (Renderer renderer in _femaleGhostRemove) RestoreFemaleGhostRenderer(renderer);
            _femaleGhostRemove.Clear();
            _femaleGhostTarget = null;
            _nextFemaleGhostScan = 0f;
        }

        private void DisposeFemaleGhost()
        {
            RestoreFemaleGhost();
            foreach (Material ghost in _femaleGhostMaterials.Values)
                if (ghost != null) Destroy(ghost);
            _femaleGhostMaterials.Clear();
        }
    }
}
