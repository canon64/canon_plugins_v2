using System;
using System.Collections.Generic;
using MainGameBlankMapAdd.Overlay;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private const string OverlayQuadName = "__SubCameraOverlayQuad";
        private string _overlayBrightnessInput;
        private const string VideoSurfacePrefix = "VideoSurface_";

        private static readonly string[] OverlayLogicalFaceNames =
            { "Front", "Back", "Left", "Right", "Floor", "Ceiling" };
        private static readonly string[] OverlayTileModeOptions = { "single", "tile" };

        private readonly OverlayRuntimeState _overlayState = new OverlayRuntimeState();
        private Mesh _overlayCombinedMesh;
        private List<GameObject> _overlayQuads => _overlayState.Quads;
        private Material _overlayMaterial { get => _overlayState.Material; set => _overlayState.Material = value; }
        private RenderTexture _cachedSubCameraRenderTexture { get => _overlayState.CachedRenderTexture; set => _overlayState.CachedRenderTexture = value; }
        private int _cachedSubCameraTextureWidth { get => _overlayState.CachedTextureWidth; set => _overlayState.CachedTextureWidth = value; }
        private int _cachedSubCameraTextureHeight { get => _overlayState.CachedTextureHeight; set => _overlayState.CachedTextureHeight = value; }
        private bool _overlayLoggedMissingTexture { get => _overlayState.LoggedMissingTexture; set => _overlayState.LoggedMissingTexture = value; }
        private string _lastBuiltOverlaySurface { get => _overlayState.LastBuiltSurface; set => _overlayState.LastBuiltSurface = value; }
        private string _lastBuiltOverlayMode { get => _overlayState.LastBuiltMode; set => _overlayState.LastBuiltMode = value; }
        private int _lastBuiltOverlayChildCount { get => _overlayState.LastBuiltChildCount; set => _overlayState.LastBuiltChildCount = value; }
        private GameObject _lastBuiltOverlayRoot { get => _overlayState.LastBuiltRoot; set => _overlayState.LastBuiltRoot = value; }
        private bool _lastOverlayEnabledState { get => _overlayState.LastEnabledState; set => _overlayState.LastEnabledState = value; }

        // Low/Mid/High の区間切替を直接出力へ反映せず、0.5秒かけて追従させる。
        private const float OverlayBeatZoneTransitionSec = 0.5f;
        private OverlayBeatZone _lastOverlayBeatZone { get => _overlayState.LastBeatZone; set => _overlayState.LastBeatZone = value; }
        private float _overlayBeatSmoothedZoneSpeed01 { get => _overlayState.BeatSmoothedZoneSpeed01; set => _overlayState.BeatSmoothedZoneSpeed01 = value; }
        private float _overlayBeatPhase01 { get => _overlayState.BeatPhase01; set => _overlayState.BeatPhase01 = value; }
        private float _overlayBeatLastBpmHz { get => _overlayState.BeatLastBpmHz; set => _overlayState.BeatLastBpmHz = value; }
        private float _lastOverlayResolvedOpacity { get => _overlayState.LastResolvedOpacity; set => _overlayState.LastResolvedOpacity = value; }

        private void UpdateSubCameraOverlay()
        {
            bool enabled = _settings != null && _settings.OverlayEnabled;

            if (!enabled || _videoRoomRoot == null)
            {
                if (_overlayQuads.Count > 0)
                    HideAllOverlayQuads();
                ResetOverlayBeatFadeState();
                _lastOverlayEnabledState = false;
                return;
            }

            if (!TryGetSubCameraRenderTextureExternal(out RenderTexture rt, out string rtReason))
            {
                if (!_overlayLoggedMissingTexture)
                {
                    LogInfo("[overlay] subcamera render texture unavailable reason=" + rtReason);
                    _overlayLoggedMissingTexture = true;
                }
                if (_overlayQuads.Count > 0)
                    HideAllOverlayQuads();
                ResetOverlayBeatFadeState();
                return;
            }
            _overlayLoggedMissingTexture = false;

            EnsureOverlayMaterial();
            if (_overlayMaterial == null)
                return;

            if (rt != _cachedSubCameraRenderTexture
                || rt.width != _cachedSubCameraTextureWidth
                || rt.height != _cachedSubCameraTextureHeight)
            {
                _cachedSubCameraRenderTexture = rt;
                _cachedSubCameraTextureWidth = rt.width;
                _cachedSubCameraTextureHeight = rt.height;
                _overlayMaterial.mainTexture = rt;
                ApplyCenterSquareCropToOverlayMaterial(rt.width, rt.height);
            }

            string targetSurface = NormalizeLogicalSurfacesCsv(_settings.OverlayTargetSurface);
            string tileMode = NormalizeTileMode(_settings.OverlayTileMode);
            int currentChildCount = _videoRoomRoot.transform.childCount;
            bool surfaceChanged = !string.Equals(targetSurface, _lastBuiltOverlaySurface, StringComparison.Ordinal);
            bool modeChanged = !string.Equals(tileMode, _lastBuiltOverlayMode, StringComparison.Ordinal);
            bool roomChanged = _lastBuiltOverlayRoot != _videoRoomRoot
                || currentChildCount != _lastBuiltOverlayChildCount;
            bool enabledRising = !_lastOverlayEnabledState;

            if (surfaceChanged || modeChanged || roomChanged || enabledRising || _overlayQuads.Count == 0)
            {
                RebuildOverlayQuads(targetSurface, tileMode);
                _lastBuiltOverlaySurface = targetSurface;
                _lastBuiltOverlayMode = tileMode;
                _lastBuiltOverlayChildCount = _videoRoomRoot.transform.childCount;
                _lastBuiltOverlayRoot = _videoRoomRoot;
            }

            ApplyOverlayOpacity(ResolveOverlayOpacityWithBeat());

            for (int i = 0; i < _overlayQuads.Count; i++)
            {
                GameObject quad = _overlayQuads[i];
                if (quad != null && !quad.activeSelf)
                    quad.SetActive(true);
            }

            _lastOverlayEnabledState = true;
        }

        private void RebuildOverlayQuads(string targetSurfaceCsv, string tileMode)
        {
            DestroyOverlayQuads();

            if (_videoRoomRoot == null)
                return;

            HashSet<string> selected = ParseLogicalFaceSet(targetSurfaceCsv);
            if (selected.Count == 0)
                return;

            foreach (string logicalName in OverlayLogicalFaceNames)
            {
                if (!selected.Contains(logicalName))
                    continue;

                List<Transform> targets = ResolveTargetVideoSurfaces(logicalName);
                if (targets.Count == 0)
                    continue;

                if (string.Equals(tileMode, "tile", StringComparison.Ordinal))
                {
                    for (int ti = 0; ti < targets.Count; ti++)
                        CreateOverlayQuadForTile(targets[ti]);
                }
                else
                {
                    CreateOverlayQuadForLogicalGroup(targets);
                }
            }

            if (string.Equals(tileMode, "tile", StringComparison.Ordinal))
                CombineOverlayTiles();

            LogInfo("[overlay] built quads count=" + _overlayQuads.Count + " surfaces=" + targetSurfaceCsv + " mode=" + tileMode);
        }

        private void CombineOverlayTiles()
        {
            if (_videoRoomRoot == null || _overlayMaterial == null || _overlayQuads.Count < 2)
                return;

            Transform room = _videoRoomRoot.transform;
            var instances = new List<CombineInstance>(_overlayQuads.Count);
            for (int i = 0; i < _overlayQuads.Count; i++)
            {
                GameObject quad = _overlayQuads[i];
                MeshFilter filter = quad != null ? quad.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null)
                    continue;
                instances.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = room.worldToLocalMatrix * quad.transform.localToWorldMatrix
                });
            }
            if (instances.Count < 2)
                return;

            Mesh combined = null;
            GameObject batch = null;
            try
            {
                combined = new Mesh { name = OverlayQuadName + "_BatchMesh" };
                combined.CombineMeshes(instances.ToArray(), true, true);
                batch = new GameObject(OverlayQuadName + "_Batch", typeof(MeshFilter), typeof(MeshRenderer));
                batch.transform.SetParent(room, worldPositionStays: false);
                batch.GetComponent<MeshFilter>().sharedMesh = combined;
                MeshRenderer renderer = batch.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = _overlayMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                for (int i = 0; i < _overlayQuads.Count; i++)
                    DestroyOverlayTile(_overlayQuads[i]);
                _overlayQuads.Clear();
                _overlayQuads.Add(batch);
                _overlayCombinedMesh = combined;
            }
            catch (Exception ex)
            {
                if (batch != null) Destroy(batch);
                if (combined != null) Destroy(combined);
                LogWarn("[overlay] tile batching failed: " + ex.Message);
            }
        }

        private void DestroyOverlayTile(GameObject quad)
        {
            if (quad == null) return;
            quad.SetActive(false);
            quad.transform.SetParent(null, worldPositionStays: false);
            Destroy(quad);
        }

        private void CreateOverlayQuadForTile(Transform target)
        {
            if (target == null)
                return;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = OverlayQuadName;
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            quad.transform.SetParent(_videoRoomRoot.transform, worldPositionStays: false);

            Vector3 normal = target.rotation * Vector3.back;
            Vector3 worldPos = target.position + normal * 0.003f;
            quad.transform.SetPositionAndRotation(worldPos, target.rotation);
            quad.transform.localScale = ConvertWorldScaleToLocalScale(quad.transform.parent, target.lossyScale);

            ApplyOverlayMaterial(quad);
            _overlayQuads.Add(quad);
        }

        private void CreateOverlayQuadForLogicalGroup(List<Transform> targets)
        {
            if (targets.Count == 0)
                return;

            // 全タイルから論理面の中央位置とサイズを計算する。
            // 同じ論理面のQuadは同じ向き(rotation)を持っているので最初のQuadの向きを採用。
            Quaternion rotation = targets[0].rotation;
            Quaternion invRotation = Quaternion.Inverse(rotation);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            Vector3 sumPos = Vector3.zero;
            int count = 0;
            foreach (Transform t in targets)
            {
                Vector3 local = invRotation * t.position;
                Vector3 size = t.lossyScale;
                float halfX = size.x * 0.5f;
                float halfY = size.y * 0.5f;
                if (local.x - halfX < minX) minX = local.x - halfX;
                if (local.x + halfX > maxX) maxX = local.x + halfX;
                if (local.y - halfY < minY) minY = local.y - halfY;
                if (local.y + halfY > maxY) maxY = local.y + halfY;
                sumPos += t.position;
                count++;
            }
            if (count == 0)
                return;

            Vector3 centerLocal = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (invRotation * (sumPos / count)).z);
            Vector3 centerWorld = rotation * centerLocal;
            float width = maxX - minX;
            float height = maxY - minY;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = OverlayQuadName;
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            quad.transform.SetParent(_videoRoomRoot.transform, worldPositionStays: false);

            Vector3 normal = rotation * Vector3.back;
            Vector3 worldPos = centerWorld + normal * 0.003f;
            quad.transform.SetPositionAndRotation(worldPos, rotation);
            quad.transform.localScale = ConvertWorldScaleToLocalScale(quad.transform.parent, new Vector3(width, height, 1f));

            ApplyOverlayMaterial(quad);
            _overlayQuads.Add(quad);
        }

        private void ApplyOverlayMaterial(GameObject quad)
        {
            if (quad == null)
                return;

            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            renderer.sharedMaterial = _overlayMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void EnsureOverlayMaterial()
        {
            if (_overlayMaterial != null)
                return;

            Shader shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null)
                shader = Shader.Find("Mobile/Particles/Alpha Blended");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            _overlayMaterial = new Material(shader)
            {
                name = "SubCameraOverlayMat",
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
            };

            if (_overlayMaterial.HasProperty("_TintColor"))
                _overlayMaterial.SetColor("_TintColor", new Color(1f, 1f, 1f, 0.5f));
            if (_overlayMaterial.HasProperty("_Color"))
                _overlayMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.5f));
            if (_overlayMaterial.HasProperty("_ZWrite"))
                _overlayMaterial.SetInt("_ZWrite", 0);
            if (_overlayMaterial.HasProperty("_Cull"))
                _overlayMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _overlayMaterial.SetOverrideTag("RenderType", "Transparent");
        }

        private void ApplyCenterSquareCropToOverlayMaterial(int width, int height)
        {
            if (_overlayMaterial == null || width <= 0 || height <= 0)
                return;

            float scaleX = 1f;
            float scaleY = 1f;
            if (width > height)
            {
                scaleX = (float)height / (float)width;
            }
            else if (height > width)
            {
                scaleY = (float)width / (float)height;
            }

            float offsetX = (1f - scaleX) * 0.5f;
            float offsetY = (1f - scaleY) * 0.5f;
            _overlayMaterial.SetTextureScale("_MainTex", new Vector2(scaleX, scaleY));
            _overlayMaterial.SetTextureOffset("_MainTex", new Vector2(offsetX, offsetY));
        }

        private static bool TryGetOverlayBeatZone(
            Integrations.IntegrationRegistry.OverlayBeatSnapshot snapshot,
            out OverlayBeatZone zone,
            out float lowIntensity,
            out float midIntensity,
            out float highIntensity)
        {
            zone = OverlayBeatZone.Unknown;
            lowIntensity = 0f;
            midIntensity = 0.5f;
            highIntensity = 1f;

            if (snapshot == null) return false;
            float rawTarget = snapshot.RawTarget;
            float low = snapshot.LowIntensity;
            float mid = snapshot.MidIntensity;
            float high = snapshot.HighIntensity;

            lowIntensity = low;
            midIntensity = mid;
            highIntensity = high;

            float dLow = Mathf.Abs(rawTarget - low);
            float dMid = Mathf.Abs(rawTarget - mid);
            float dHigh = Mathf.Abs(rawTarget - high);

            if (dLow <= dMid && dLow <= dHigh)
                zone = OverlayBeatZone.Low;
            else if (dHigh <= dLow && dHigh <= dMid)
                zone = OverlayBeatZone.High;
            else
                zone = OverlayBeatZone.Mid;

            return true;
        }

        private bool TryGetOverlayBeatLoopTarget(out float bpmHz, out float zoneSpeed01, out OverlayBeatZone zone)
        {
            bpmHz = 0f;
            zoneSpeed01 = 0f;
            zone = OverlayBeatZone.Unknown;
            if (!_integrations.TryGetOverlayBeatSnapshot(out var snapshot)) return false;
            int bpm = snapshot.Bpm;
            if (!TryGetOverlayBeatZone(snapshot, out zone, out float low, out float mid, out float high)) return false;

            float rawZoneSpeed01 = zone == OverlayBeatZone.Low ? low
                : zone == OverlayBeatZone.Mid ? mid
                : zone == OverlayBeatZone.High ? high : -1f;
            if (rawZoneSpeed01 < 0f) return false;

            bpmHz = Mathf.Max(1, bpm) / 60f;
            zoneSpeed01 = Mathf.Clamp01(rawZoneSpeed01);
            return bpmHz > 0f;
        }

        private float GetBeatPulse01ForOverlay(float bpmHz, float speed01)
        {
            if (speed01 <= 0.0001f || bpmHz <= 0f)
                return 0f;

            _overlayBeatPhase01 = Mathf.Repeat(_overlayBeatPhase01 + bpmHz * speed01 * Time.unscaledDeltaTime, 1f);
            return Mathf.Clamp01(Mathf.Sin(_overlayBeatPhase01 * Mathf.PI));
        }

        private float ResolveOverlayOpacityWithBeat()
        {
            float baseOpacity = _settings != null ? Mathf.Clamp01(_settings.OverlayOpacity) : 0.5f;
            if (_settings == null || !_settings.OverlayBeatOpacityEnabled)
            {
                ResetOverlayBeatFadeState();
                _lastOverlayResolvedOpacity = baseOpacity;
                return baseOpacity;
            }

            float targetSpeed01 = 0f;
            float bpmHz = 0f;
            OverlayBeatZone currentZone = OverlayBeatZone.Unknown;
            if (TryGetOverlayBeatLoopTarget(out float resolvedBpmHz, out float resolvedSpeed01, out OverlayBeatZone resolvedZone))
            {
                bpmHz = resolvedBpmHz;
                _overlayBeatLastBpmHz = bpmHz;
                targetSpeed01 = resolvedSpeed01;
                currentZone = resolvedZone;
            }
            else
            {
                bpmHz = _overlayBeatLastBpmHz;
            }

            if (_overlayBeatSmoothedZoneSpeed01 < 0f || _lastOverlayBeatZone == OverlayBeatZone.Unknown)
                _overlayBeatSmoothedZoneSpeed01 = targetSpeed01;
            else
                _overlayBeatSmoothedZoneSpeed01 = Mathf.MoveTowards(
                    _overlayBeatSmoothedZoneSpeed01,
                    targetSpeed01,
                    Time.unscaledDeltaTime / OverlayBeatZoneTransitionSec);
            _lastOverlayBeatZone = currentZone;

            float speed01 = Mathf.Clamp01(_overlayBeatSmoothedZoneSpeed01);
            float pulse01 = GetBeatPulse01ForOverlay(bpmHz, speed01);
            float min = Mathf.Clamp01(_settings.OverlayBeatOpacityMin);
            float max = Mathf.Clamp01(_settings.OverlayBeatOpacityMax);

            float normalOpacity = _settings.OverlayBeatOpacityInverted
                ? Mathf.Lerp(max, min, pulse01)
                : Mathf.Lerp(min, max, pulse01);

            float result = normalOpacity * speed01;
            result = Mathf.Clamp01(result);
            _lastOverlayResolvedOpacity = result;
            return result;
        }

        private void ResetOverlayBeatFadeState()
        {
            _lastOverlayBeatZone = OverlayBeatZone.Unknown;
            _overlayBeatSmoothedZoneSpeed01 = -1f;
            _overlayBeatPhase01 = 0f;
            _overlayBeatLastBpmHz = 0f;
            _lastOverlayResolvedOpacity = _settings != null ? Mathf.Clamp01(_settings.OverlayOpacity) : 0.5f;
        }

        private void ApplyOverlayOpacity(float opacity)
        {
            if (_overlayMaterial == null)
                return;

            float clamped = Mathf.Clamp01(opacity);
            float brightness = _settings != null ? Mathf.Clamp(_settings.OverlayBrightness, 0f, 2f) : 1f;
            // Particles/Alpha Blended 系は _TintColor.a が乗算で効く（半輝度なので2倍補正）
            if (_overlayMaterial.HasProperty("_TintColor"))
            {
                Color color = _overlayMaterial.GetColor("_TintColor");
                float alpha = clamped * 0.5f;
                if (color.a != alpha || color.r != brightness || color.g != brightness || color.b != brightness)
                {
                    color = new Color(brightness, brightness, brightness, alpha);
                    _overlayMaterial.SetColor("_TintColor", color);
                }
            }
            if (_overlayMaterial.HasProperty("_Color"))
            {
                Color color = _overlayMaterial.color;
                if (color.a != clamped || color.r != brightness || color.g != brightness || color.b != brightness)
                {
                    color = new Color(brightness, brightness, brightness, clamped);
                    _overlayMaterial.color = color;
                }
            }
        }

        private List<Transform> ResolveTargetVideoSurfaces(string logicalName)
        {
            var result = new List<Transform>();
            if (_videoRoomRoot == null || string.IsNullOrEmpty(logicalName))
                return result;

            string physicalName = LogicalToPhysicalName(logicalName);
            if (string.IsNullOrEmpty(physicalName))
                return result;

            Transform root = _videoRoomRoot.transform;
            int count = root.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.name == null)
                    continue;
                if (!child.name.StartsWith(VideoSurfacePrefix, StringComparison.Ordinal))
                    continue;
                string surfaceName = child.name.Substring(VideoSurfacePrefix.Length);
                if (!IsSamePhysicalSurface(surfaceName, physicalName))
                    continue;
                result.Add(child);
            }
            return result;
        }

        private static string LogicalToPhysicalName(string logicalName)
        {
            switch (logicalName)
            {
                case "Front": return "WallFront";
                case "Back": return "WallBack";
                case "Left": return "WallLeft";
                case "Right": return "WallRight";
                case "Floor": return "Floor";
                case "Ceiling": return "Ceiling";
                default: return string.Empty;
            }
        }

        private static bool IsSamePhysicalSurface(string surfaceName, string physicalPrefix)
        {
            if (string.IsNullOrEmpty(surfaceName) || string.IsNullOrEmpty(physicalPrefix))
                return false;

            // 完全一致 (タイル分割なし) または "physicalPrefix_数字" のパターン
            if (string.Equals(surfaceName, physicalPrefix, StringComparison.Ordinal))
                return true;
            if (surfaceName.Length > physicalPrefix.Length + 1
                && surfaceName.StartsWith(physicalPrefix + "_", StringComparison.Ordinal))
            {
                string rest = surfaceName.Substring(physicalPrefix.Length + 1);
                int dummy;
                return int.TryParse(rest, out dummy);
            }
            return false;
        }

        private static string NormalizeLogicalSurfacesCsv(string raw)
        {
            return OverlaySurfaceRules.Normalize(raw);
        }

        private static HashSet<string> ParseLogicalFaceSet(string raw)
        {
            return OverlaySurfaceRules.Parse(raw);
        }

        private static string NormalizeTileMode(string raw)
        {
            return OverlaySurfaceRules.NormalizeTileMode(raw);
        }

        private static Vector3 ConvertWorldScaleToLocalScale(Transform parent, Vector3 worldScale)
        {
            if (parent == null)
                return worldScale;
            Vector3 parentLossy = parent.lossyScale;
            float x = Mathf.Approximately(parentLossy.x, 0f) ? worldScale.x : worldScale.x / parentLossy.x;
            float y = Mathf.Approximately(parentLossy.y, 0f) ? worldScale.y : worldScale.y / parentLossy.y;
            float z = Mathf.Approximately(parentLossy.z, 0f) ? worldScale.z : worldScale.z / parentLossy.z;
            return new Vector3(x, y, z);
        }

        private void HideAllOverlayQuads()
        {
            for (int i = 0; i < _overlayQuads.Count; i++)
            {
                GameObject q = _overlayQuads[i];
                if (q != null && q.activeSelf)
                    q.SetActive(false);
            }
        }

        private void DestroyOverlayQuads()
        {
            for (int i = 0; i < _overlayQuads.Count; i++)
                DestroyOverlayTile(_overlayQuads[i]);
            _overlayQuads.Clear();
            if (_overlayCombinedMesh != null)
                Destroy(_overlayCombinedMesh);
            _overlayCombinedMesh = null;
        }

        private void DestroyOverlayQuad()
        {
            DestroyOverlayQuads();

            if (_overlayMaterial != null)
            {
                Destroy(_overlayMaterial);
                _overlayMaterial = null;
            }

            _cachedSubCameraRenderTexture = null;
            _cachedSubCameraTextureWidth = 0;
            _cachedSubCameraTextureHeight = 0;
            _overlayLoggedMissingTexture = false;
            _integrations.ResetSubCameraProbeLogState();
            _lastBuiltOverlaySurface = string.Empty;
            _lastBuiltOverlayMode = string.Empty;
            _lastBuiltOverlayChildCount = -1;
            _lastBuiltOverlayRoot = null;
            _lastOverlayEnabledState = false;
            ResetOverlayBeatFadeState();
        }

        private bool TryGetSubCameraRenderTextureExternal(out RenderTexture renderTexture, out string reason)
        {
            return _integrations.TryGetSubCameraRenderTexture(out renderTexture, out reason);
        }

        private void DrawSubCameraOverlaySection(float boxX, float boxY, float boxW, float boxH)
        {
            float sectionGap = 8f;
            float solidBoxW = Mathf.Clamp(boxW * 0.32f, 260f, 420f);
            float overlayBoxW = boxW - solidBoxW - sectionGap;
            if (overlayBoxW < 620f)
            {
                solidBoxW = Mathf.Max(240f, boxW - sectionGap - 620f);
                overlayBoxW = boxW - solidBoxW - sectionGap;
            }
            if (overlayBoxW < 520f || solidBoxW < 220f)
            {
                overlayBoxW = boxW;
                solidBoxW = 0f;
            }

            float overlayBoxX = boxX;
            GUI.Box(new Rect(overlayBoxX, boxY, overlayBoxW, boxH), "SUBCAMERA OVERLAY");
            if (solidBoxW > 0f)
                DrawSolidRoomColorSection(overlayBoxX + overlayBoxW + sectionGap, boxY, solidBoxW, boxH);

            float rowH = 22f;
            float row1Y = boxY + 22f;
            float row2Y = row1Y + rowH + 2f;
            float contentX = overlayBoxX + 8f;

            // 1段目: Enabled / Mode[Single/Tile] / Opacity slider / 数値
            float toggleW = 80f;
            bool currentEnabled = _settings != null && _settings.OverlayEnabled;
            bool nextEnabled = GUI.Toggle(new Rect(contentX, row1Y, toggleW, rowH), currentEnabled, "Enabled");
            if (nextEnabled != currentEnabled && _settings != null)
            {
                _settings.OverlayEnabled = nextEnabled;
                LogInfo("[overlay] enabled=" + nextEnabled);
                SaveSubCameraOverlaySettings();
            }

            float modeLabelW = 36f;
            float modeLabelX = contentX + toggleW + 6f;
            GUI.Label(new Rect(modeLabelX, row1Y, modeLabelW, rowH), "Mode");

            float modeBtnW = 56f;
            float modeBtnGap = 2f;
            string currentMode = NormalizeTileMode(_settings != null ? _settings.OverlayTileMode : "single");
            for (int i = 0; i < OverlayTileModeOptions.Length; i++)
            {
                string mode = OverlayTileModeOptions[i];
                bool selected = string.Equals(mode, currentMode, StringComparison.Ordinal);
                Rect modeRect = new Rect(modeLabelX + modeLabelW + i * (modeBtnW + modeBtnGap), row1Y, modeBtnW, rowH);
                Color prevColor = GUI.backgroundColor;
                if (selected)
                    GUI.backgroundColor = Color.cyan;
                if (GUI.Button(modeRect, mode == "single" ? "Single" : "Tile"))
                {
                    if (_settings != null && !selected)
                    {
                        _settings.OverlayTileMode = mode;
                        LogInfo("[overlay] mode=" + mode);
                        SaveSubCameraOverlaySettings();
                    }
                }
                GUI.backgroundColor = prevColor;
            }

            float opacityRowX = modeLabelX + modeLabelW + OverlayTileModeOptions.Length * (modeBtnW + modeBtnGap) + 8f;
            float opacityLabelW = 56f;
            GUI.Label(new Rect(opacityRowX, row1Y, opacityLabelW, rowH), "Opacity");

            float opacityInputW = 50f;
            float opacityInputX = overlayBoxX + overlayBoxW - 8f - opacityInputW;
            float opacitySliderX = opacityRowX + opacityLabelW + 4f;
            float opacitySliderW = Mathf.Max(40f, opacityInputX - 4f - opacitySliderX);

            float currentOpacity = _settings != null ? Mathf.Clamp01(_settings.OverlayOpacity) : 0.5f;
            float sliderOpacity = GUI.HorizontalSlider(
                new Rect(opacitySliderX, row1Y + 4f, opacitySliderW, rowH),
                currentOpacity, 0f, 1f);
            if (Mathf.Abs(sliderOpacity - currentOpacity) > 0.0001f && _settings != null)
            {
                _settings.OverlayOpacity = Mathf.Clamp01(sliderOpacity);
                _overlayOpacityInput = _settings.OverlayOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                SaveSubCameraOverlaySettings();
                currentOpacity = _settings.OverlayOpacity;
            }

            string opacityCtrlName = "OverlayOpacityInput";
            if (string.IsNullOrEmpty(_overlayOpacityInput) || GUI.GetNameOfFocusedControl() != opacityCtrlName)
            {
                _overlayOpacityInput = currentOpacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
            GUI.SetNextControlName(opacityCtrlName);
            string nextInput = GUI.TextField(new Rect(opacityInputX, row1Y, opacityInputW, rowH), _overlayOpacityInput);
            if (nextInput != _overlayOpacityInput)
            {
                _overlayOpacityInput = nextInput;
                if (float.TryParse(nextInput, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float typed))
                {
                    float clamped = Mathf.Clamp01(typed);
                    if (_settings != null && Mathf.Abs(clamped - _settings.OverlayOpacity) > 0.0001f)
                    {
                        _settings.OverlayOpacity = clamped;
                        SaveSubCameraOverlaySettings();
                    }
                }
            }

            // 2段目: 面チェックボックス6個 + All / None ボタン
            HashSet<string> selected2 = ParseLogicalFaceSet(_settings != null ? _settings.OverlayTargetSurface : "All");
            float faceLabelW = 36f;
            GUI.Label(new Rect(contentX, row2Y, faceLabelW, rowH), "Faces");

            float faceCheckW = 70f;
            float faceCheckGap = 2f;
            float faceX = contentX + faceLabelW;
            bool changed = false;
            for (int i = 0; i < OverlayLogicalFaceNames.Length; i++)
            {
                string face = OverlayLogicalFaceNames[i];
                bool isOn = selected2.Contains(face);
                Rect faceRect = new Rect(faceX, row2Y, faceCheckW, rowH);
                bool nextOn = GUI.Toggle(faceRect, isOn, face);
                if (nextOn != isOn)
                {
                    if (nextOn) selected2.Add(face);
                    else selected2.Remove(face);
                    changed = true;
                }
                faceX += faceCheckW + faceCheckGap;
            }

            float allBtnW = 50f;
            if (GUI.Button(new Rect(faceX, row2Y, allBtnW, rowH), "All"))
            {
                selected2.Clear();
                foreach (string face in OverlayLogicalFaceNames)
                    selected2.Add(face);
                changed = true;
            }
            faceX += allBtnW + faceCheckGap;
            if (GUI.Button(new Rect(faceX, row2Y, allBtnW, rowH), "None"))
            {
                selected2.Clear();
                changed = true;
            }

            if (changed && _settings != null)
            {
                _settings.OverlayTargetSurface = SerializeLogicalFaceSet(selected2);
                LogInfo("[overlay] faces=" + _settings.OverlayTargetSurface);
                SaveSubCameraOverlaySettings();
            }

            // 3段目: 拍透明度連動（BeatOp / Min / Max / Inverted）
            float row3Y = row2Y + rowH + 2f;
            float beatToggleW = 90f;
            bool currentBeatEnabled = _settings != null && _settings.OverlayBeatOpacityEnabled;
            bool nextBeatEnabled = GUI.Toggle(new Rect(contentX, row3Y, beatToggleW, rowH), currentBeatEnabled, "拍Op連動");
            if (nextBeatEnabled != currentBeatEnabled && _settings != null)
            {
                _settings.OverlayBeatOpacityEnabled = nextBeatEnabled;
                LogInfo("[overlay] beat-opacity=" + nextBeatEnabled);
                SaveSubCameraOverlaySettings();
            }

            float minLabelW = 28f;
            float minLabelX = contentX + beatToggleW + 4f;
            GUI.Label(new Rect(minLabelX, row3Y, minLabelW, rowH), "Min");

            float minSliderW = 100f;
            float minSliderX = minLabelX + minLabelW;
            float currentMin = _settings != null ? Mathf.Clamp01(_settings.OverlayBeatOpacityMin) : 0.1f;
            float nextMin = GUI.HorizontalSlider(new Rect(minSliderX, row3Y + 4f, minSliderW, rowH), currentMin, 0f, 1f);
            if (Mathf.Abs(nextMin - currentMin) > 0.0001f && _settings != null)
            {
                _settings.OverlayBeatOpacityMin = Mathf.Clamp01(nextMin);
                SaveSubCameraOverlaySettings();
            }
            GUI.Label(new Rect(minSliderX + minSliderW + 2f, row3Y, 36f, rowH), currentMin.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            float maxLabelX = minSliderX + minSliderW + 42f;
            float maxLabelW = 30f;
            GUI.Label(new Rect(maxLabelX, row3Y, maxLabelW, rowH), "Max");
            float maxSliderW = 100f;
            float maxSliderX = maxLabelX + maxLabelW;
            float currentMax = _settings != null ? Mathf.Clamp01(_settings.OverlayBeatOpacityMax) : 1.0f;
            float nextMax = GUI.HorizontalSlider(new Rect(maxSliderX, row3Y + 4f, maxSliderW, rowH), currentMax, 0f, 1f);
            if (Mathf.Abs(nextMax - currentMax) > 0.0001f && _settings != null)
            {
                _settings.OverlayBeatOpacityMax = Mathf.Clamp01(nextMax);
                SaveSubCameraOverlaySettings();
            }
            GUI.Label(new Rect(maxSliderX + maxSliderW + 2f, row3Y, 36f, rowH), currentMax.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            float invertedX = maxSliderX + maxSliderW + 42f;
            float invertedW = 70f;
            bool currentInverted = _settings != null && _settings.OverlayBeatOpacityInverted;
            bool nextInverted = GUI.Toggle(new Rect(invertedX, row3Y, invertedW, rowH), currentInverted, "反転");
            if (nextInverted != currentInverted && _settings != null)
            {
                _settings.OverlayBeatOpacityInverted = nextInverted;
                LogInfo("[overlay] beat-opacity-inverted=" + nextInverted);
                SaveSubCameraOverlaySettings();
            }
            DrawOverlayBrightness(contentX, row3Y + rowH + 2f, overlayBoxW - 16f);
        }

        private void DrawOverlayBrightness(float x, float y, float width)
        {
            if (_settings == null) return;
            GUI.Label(new Rect(x, y, 120f, 22f), "壁映像の明るさ");
            float current = _settings.OverlayBrightness;
            float next = GUI.HorizontalSlider(new Rect(x + 124f, y + 4f, width - 184f, 22f), current, 0f, 2f);
            const string control = "WallOverlayBrightness";
            if (_overlayBrightnessInput == null || GUI.GetNameOfFocusedControl() != control)
                _overlayBrightnessInput = current.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            GUI.SetNextControlName(control);
            string input = GUI.TextField(new Rect(x + width - 54f, y, 54f, 22f), _overlayBrightnessInput);
            if (input != _overlayBrightnessInput)
            {
                _overlayBrightnessInput = input;
                float parsed;
                if (float.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                    && !float.IsNaN(parsed) && !float.IsInfinity(parsed)) next = Mathf.Clamp(parsed, 0f, 2f);
            }
            if (Mathf.Abs(next - current) > 0.0001f)
            {
                _settings.OverlayBrightness = next;
                ApplyOverlayOpacity(_lastOverlayResolvedOpacity);
                SaveSubCameraOverlaySettings();
            }
        }

        private void DrawSolidRoomColorSection(float boxX, float boxY, float boxW, float boxH)
        {
            GUI.Box(new Rect(boxX, boxY, boxW, boxH), "SOLID COLOR");
            if (_settings == null)
                return;

            float rowH = 22f;
            float contentX = boxX + 8f;
            float contentW = Mathf.Max(40f, boxW - 16f);
            float rowY = boxY + 22f;

            bool currentEnabled = _settings.SolidRoomColorEnabled;
            bool nextEnabled = GUI.Toggle(new Rect(contentX, rowY, contentW, rowH), currentEnabled, "全面単色モード");
            if (nextEnabled != currentEnabled)
            {
                _settings.SolidRoomColorEnabled = nextEnabled;
                SaveSubCameraOverlaySettings();
                UpdateSolidRoomColorMode();
                LogInfo("[solid-room] enabled=" + nextEnabled);
            }

            rowY += rowH + 4f;
            bool changed = false;
            changed |= DrawSolidColorSlider("R", ref _settings.SolidRoomColorR, contentX, rowY, contentW);
            rowY += rowH + 2f;
            changed |= DrawSolidColorSlider("G", ref _settings.SolidRoomColorG, contentX, rowY, contentW);
            rowY += rowH + 2f;
            changed |= DrawSolidColorSlider("B", ref _settings.SolidRoomColorB, contentX, rowY, contentW);

            if (changed)
            {
                _settings.SolidRoomColorR = Mathf.Clamp01(_settings.SolidRoomColorR);
                _settings.SolidRoomColorG = Mathf.Clamp01(_settings.SolidRoomColorG);
                _settings.SolidRoomColorB = Mathf.Clamp01(_settings.SolidRoomColorB);
                SaveSubCameraOverlaySettings();
                UpdateSolidRoomColorMode();
            }

        }

        private static bool DrawSolidColorSlider(string label, ref float value, float x, float y, float width)
        {
            float rowH = 22f;
            float labelW = 14f;
            float valueW = 42f;
            float sliderX = x + labelW + 4f;
            float sliderW = Mathf.Max(40f, width - labelW - valueW - 10f);

            GUI.Label(new Rect(x, y, labelW, rowH), label);
            float current = Mathf.Clamp01(value);
            float next = GUI.HorizontalSlider(new Rect(sliderX, y + 4f, sliderW, rowH), current, 0f, 1f);
            GUI.Label(
                new Rect(sliderX + sliderW + 4f, y, valueW, rowH),
                current.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            if (Mathf.Abs(next - current) <= 0.0001f)
                return false;

            value = Mathf.Clamp01(next);
            return true;
        }

        private static string SerializeLogicalFaceSet(HashSet<string> set)
        {
            return OverlaySurfaceRules.Serialize(set);
        }

        private void SaveSubCameraOverlaySettings()
        {
            try
            {
                _settingsRepository.SaveCurrent(_settings);
            }
            catch (Exception ex)
            {
                LogWarn("[overlay] save settings failed: " + ex.Message);
            }
        }

        internal IEnumerable<string> EnumerateVideoSurfaceNames()
        {
            if (_videoRoomRoot == null)
                yield break;

            Transform root = _videoRoomRoot.transform;
            int count = root.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.name == null)
                    continue;
                if (!child.name.StartsWith(VideoSurfacePrefix, StringComparison.Ordinal))
                    continue;
                string surfaceName = child.name.Substring(VideoSurfacePrefix.Length);
                if (string.IsNullOrWhiteSpace(surfaceName))
                    continue;
                yield return surfaceName;
            }
        }
    }
}
