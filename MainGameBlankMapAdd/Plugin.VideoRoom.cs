using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using MainGameBlankMapAdd.Room;
using MainGameTransformGizmo;
using UnityEngine;
using UnityEngine.Video;
using MainGameBlankMapAdd.Media.Video;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private readonly List<AudioSource> _videoRoomVoiceSources = new List<AudioSource>();
        private readonly Dictionary<int, VoiceSourceClassification> _voiceSourceClassifications =
            new Dictionary<int, VoiceSourceClassification>();
        private float _nextVideoRoomVoiceSourceScanTime;

        private struct VoiceSourceClassification
        {
            public AudioSource Source;
            public bool IsVoice;
            public float ValidUntil;
        }

        private void EnsureVideoRoom(BaseMap map)
        {
            if (map == null || map.mapRoot == null) return;

            int rootId = map.mapRoot.GetInstanceID();
            if (_videoRoomRoot != null && _roomResourceScope?.SourceMapRootId == rootId)
                return;

            TryApplySavedRoomLayoutForCurrentSelection("ensure-video-room");

            DestroyVideoRoom();

            _roomResourceScope = new MediaRoomResourceScope(rootId);
            _videoRoomRoot = new GameObject("__BlankMapVideoRoom");
            _roomResourceScope.OwnRoot(_videoRoomRoot);
            _gizmo = TransformGizmoApi.Attach(_videoRoomRoot);
            if (_gizmo != null)
                _gizmo.DragStateChanged += NotifyGizmoDragState;
            _videoRoomRoot.transform.SetParent(map.mapRoot.transform, false);
            _videoRoomRoot.transform.localPosition = Vector3.zero;
            _videoRoomRoot.transform.localScale =
                Vector3.one * Mathf.Clamp(_playbackRoomScale, 0.25f, 4f);
            // 菫晏ｭ俶ｸ医∩蝗櫁ｻ｢繧帝←逕ｨ
            _videoRoomRoot.transform.rotation = Quaternion.Euler(
                _settings.VideoRoomRotationX,
                _settings.VideoRoomRotationY,
                _settings.VideoRoomRotationZ);

            EnsureFemaleCharaRef();
            float width  = Mathf.Max(1f, _settings.RoomWidth);
            float depth  = Mathf.Max(1f, _settings.RoomDepth);
            float height = Mathf.Max(1f, _settings.RoomHeight);
            float cubeSide = GetCubeSideLength(width, depth, height);
            if (_femaleChara != null)
            {
                var fp = _femaleChara.transform.position;
                _femaleBaseY = fp.y;
                _hasFemaleBaseY = true;
                // Center anchor for both sphere and cube.
                _videoRoomRoot.transform.position = new Vector3(
                    fp.x + _settings.VideoRoomOffsetX,
                    _femaleBaseY + _settings.VideoRoomOffsetY,
                    fp.z + _settings.VideoRoomOffsetZ);
            }
            else
            {
                _hasFemaleBaseY = false;
            }

            string sharedVideoPath = ResolveVideoPath(_settings.VideoPath);

            if (_settings.UseSphere)
            {
                CreateSphereSurface(_videoRoomRoot.transform, sharedVideoPath);
                LogInfo($"video room created mapNo={map.no} sphere radius={_settings.SphereRadius:F1}");
            }
            else
            {
                CreateCubeVideoSurfaces(cubeSide, sharedVideoPath);
                LogInfo($"video room created mapNo={map.no} cubeSide={cubeSide:F1}");
            }

            ApplyRoomReverb(width, depth, height);
            UpdateSolidRoomColorMode();

            // 蠎ｧ讓吶・隗貞ｺｦ繝ｭ繧ｰ
            LogInfo(
                $"video room world pos={_videoRoomRoot.transform.position} " +
                $"rot={_videoRoomRoot.transform.eulerAngles}");

            if (IsAstralModeSuppressed)
                ApplyAstralRoomOffset(-100f);

            var chaControls = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            foreach (var c in chaControls)
            {
                if (c == null) continue;
                LogInfo(
                    $"chara name={c.name} sex={c.sex} " +
                    $"pos={c.transform.position} rot={c.transform.eulerAngles}");
            }
        }

        private void CreateCubeVideoSurfaces(float cubeSide, string sharedVideoPath)
        {
            string wallPath = PickVideoPathForSurface("wall", sharedVideoPath);
            string floorPath = PickVideoPathForSurface("floor", sharedVideoPath);
            string ceilingPath = PickVideoPathForSurface("ceiling", sharedVideoPath);
            bool audioAssigned = false;
            float half = cubeSide * 0.5f;
            int tileCount = ResolveCubeFaceTileCount();
            ResolveCubeFaceTileGrid(tileCount, out int columns, out int rows);
            Vector2 panelSize = new Vector2(cubeSide / columns, cubeSide / rows);

            CreateCubeSurfaceTiles(
                "WallBack",
                new Vector3(0f, 0f, -half),
                Quaternion.Euler(0f, 180f, 0f),
                panelSize,
                wallPath,
                tileCount,
                ref audioAssigned);

            CreateCubeSurfaceTiles(
                "WallFront",
                new Vector3(0f, 0f, half),
                Quaternion.identity,
                panelSize,
                wallPath,
                tileCount,
                ref audioAssigned);

            CreateCubeSurfaceTiles(
                "WallLeft",
                new Vector3(-half, 0f, 0f),
                Quaternion.Euler(0f, -90f, 0f),
                panelSize,
                wallPath,
                tileCount,
                ref audioAssigned);

            CreateCubeSurfaceTiles(
                "WallRight",
                new Vector3(half, 0f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                panelSize,
                wallPath,
                tileCount,
                ref audioAssigned);

            CreateCubeSurfaceTiles(
                "Floor",
                new Vector3(0f, -half, 0f),
                Quaternion.Euler(90f, 0f, 0f),
                panelSize,
                floorPath,
                tileCount,
                ref audioAssigned);

            CreateCubeSurfaceTiles(
                "Ceiling",
                new Vector3(0f, half, 0f),
                Quaternion.Euler(-90f, 0f, 0f),
                panelSize,
                ceilingPath,
                tileCount,
                ref audioAssigned);
        }

        private bool TryRefreshCubeSurfaceTilesInPlace(out string detail)
        {
            detail = string.Empty;

            if (_settings == null)
            {
                detail = "settings-null";
                return false;
            }

            if (_videoRoomRoot == null)
            {
                detail = "room-null";
                return false;
            }

            if (_settings.UseSphere)
            {
                detail = "sphere-mode";
                return true;
            }

            var surfaceObjects = new List<GameObject>();
            var detachedMaterials = new HashSet<Material>();
            Transform roomTr = _videoRoomRoot.transform;
            for (int i = 0; i < roomTr.childCount; i++)
            {
                Transform child = roomTr.GetChild(i);
                if (child == null || child.gameObject == null)
                    continue;
                if (!child.name.StartsWith("VideoSurface_", StringComparison.Ordinal))
                    continue;

                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                    detachedMaterials.Add(renderer.sharedMaterial);
                surfaceObjects.Add(child.gameObject);
            }

            if (surfaceObjects.Count == 0)
            {
                detail = "surface-not-found";
                return false;
            }

            DetachSurfaceMaterialsFromBindings(detachedMaterials);
            for (int i = 0; i < surfaceObjects.Count; i++)
                Destroy(surfaceObjects[i]);

            float cubeSide = GetCubeSideLength();
            string sharedVideoPath = ResolveCurrentSharedVideoPathForRetile();
            CreateCubeVideoSurfaces(cubeSide, sharedVideoPath);

            detail = $"retiled surfaces={surfaceObjects.Count} tiles={ResolveCubeFaceTileCount()}";
            return true;
        }

        private void DetachSurfaceMaterialsFromBindings(HashSet<Material> detachedMaterials)
        {
            foreach (var kv in _videoBindings)
            {
                VideoBinding binding = kv.Value;
                if (binding?.Materials == null)
                    continue;

                for (int i = binding.Materials.Count - 1; i >= 0; i--)
                {
                    Material mat = binding.Materials[i];
                    if (mat == null ||
                        (detachedMaterials != null && detachedMaterials.Contains(mat)))
                    {
                        binding.Materials.RemoveAt(i);
                    }
                }
            }
        }

        private string ResolveCurrentSharedVideoPathForRetile()
        {
            if (_mainVideoPlayer != null)
            {
                string current = _mainVideoPlayer.url;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    if (current.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                        return Uri.UnescapeDataString(current.Substring(8));
                    return current;
                }
            }

            return ResolveVideoPath(_settings?.VideoPath);
        }

        private static float GetCubeSideLength(float width, float depth, float height)
        {
            return Mathf.Max(1f, Mathf.Max(width, Mathf.Max(depth, height)));
        }

        private float GetCubeSideLength()
        {
            return GetCubeSideLength(
                _settings.RoomWidth,
                _settings.RoomDepth,
                _settings.RoomHeight);
        }

        private int ResolveCubeFaceTileCount()
        {
            switch (_settings.CubeFaceTileCount)
            {
                case 4:
                case 9:
                case 16:
                case 25:
                    return _settings.CubeFaceTileCount;
                default:
                    return 1;
            }
        }

        private static void ResolveCubeFaceTileGrid(int tileCount, out int columns, out int rows)
        {
            int side = Mathf.RoundToInt(Mathf.Sqrt(Mathf.Max(1, tileCount)));
            if (side * side != tileCount)
            {
                side = 1;
            }

            columns = side;
            rows = side;
        }

        private void CreateCubeSurfaceTiles(
            string surfaceName,
            Vector3 localPos,
            Quaternion localRot,
            Vector2 size,
            string videoPath,
            int tileCount,
            ref bool audioAssigned)
        {
            ResolveCubeFaceTileGrid(tileCount, out int columns, out int rows);

            Vector3 localRight = localRot * Vector3.right;
            Vector3 localUp = localRot * Vector3.up;
            float startX = -((columns - 1) * size.x * 0.5f);
            float startY = ((rows - 1) * size.y * 0.5f);
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    if (index >= tileCount)
                    {
                        return;
                    }

                    Vector3 offset = localRight * (startX + col * size.x)
                                   + localUp * (startY - row * size.y);
                    Vector3 tilePos = localPos + offset;
                    string tileName = tileCount == 1
                        ? surfaceName
                        : $"{surfaceName}_{index + 1}";

                    bool withAudio = !audioAssigned && !string.IsNullOrWhiteSpace(videoPath);
                    CreateVideoSurface(
                        _videoRoomRoot.transform,
                        tileName,
                        tilePos,
                        localRot,
                        size,
                        videoPath,
                        withAudio);
                    if (withAudio)
                    {
                        audioAssigned = true;
                    }

                    index++;
                }
            }
        }

        private void ApplyRoomReverb(float width, float depth, float height)
        {
            if (_videoRoomRoot == null) return;

            LogInfo(
                $"[reverb] apply request enabled={_settings.EnableVoiceReverb} " +
                $"preset={_settings.VoiceReverbPreset}");

            if (!_settings.EnableVoiceReverb)
            {
                if (_reverbZoneObject != null)
                {
                    Destroy(_reverbZoneObject);
                    _reverbZoneObject = null;
                    _voiceReverbZone = null;
                    LogInfo("[reverb] disabled (zone destroyed)");
                }
                else if (_voiceReverbZone != null)
                {
                    Destroy(_voiceReverbZone);
                    _voiceReverbZone = null;
                    LogInfo("[reverb] disabled (legacy zone destroyed)");
                }
                LogReverbDiagnostics("disabled");
                return;
            }

            if (_voiceReverbZone != null && _voiceReverbZone.gameObject == _videoRoomRoot)
            {
                // Migrate old layout where zone was attached to room root.
                Destroy(_voiceReverbZone);
                _voiceReverbZone = null;
                LogInfo("[reverb] migrated legacy root zone -> child zone object");
            }

            EnsureReverbZoneObject();
            if (_voiceReverbZone == null) return;

            AudioReverbPreset preset = ParseReverbPreset(_settings.VoiceReverbPreset);
            // 距離フェードは使わない: zone を実質無限大にして「ON なら常にフル」にする。
            const float minDistance = 9999f;
            const float maxDistance = 10000f;

            _voiceReverbZone.reverbPreset = preset;
            _voiceReverbZone.minDistance = minDistance;
            _voiceReverbZone.maxDistance = maxDistance;
            _voiceReverbZone.enabled = true;
            bool synced = SyncReverbZoneToFemale(true);
            if (!synced)
            {
                // Fallback while female is not resolved yet.
                _voiceReverbZone.transform.localPosition = Vector3.zero;
                LogWarn("[reverb] female position not found, temporary fallback to room origin");
            }

            LogInfo(
                $"[reverb] enabled preset={preset} min={minDistance:F1} max={maxDistance:F1} " +
                $"room=({width:F1},{depth:F1},{height:F1}) zonePos={_voiceReverbZone.transform.position}");
            LogReverbDiagnostics("enabled");
        }

        private void EnsureReverbZoneObject()
        {
            if (_videoRoomRoot == null) return;

            if (_reverbZoneObject == null)
            {
                Transform existing = _videoRoomRoot.transform.Find("__VoiceReverbZone");
                if (existing != null)
                    _reverbZoneObject = existing.gameObject;
            }

            if (_reverbZoneObject == null)
            {
                _reverbZoneObject = new GameObject("__VoiceReverbZone");
                _reverbZoneObject.transform.SetParent(_videoRoomRoot.transform, false);
                _reverbZoneObject.transform.localPosition = Vector3.zero;
                _reverbZoneObject.transform.localRotation = Quaternion.identity;
            }

            if (_voiceReverbZone == null)
                _voiceReverbZone = _reverbZoneObject.GetComponent<AudioReverbZone>();
            if (_voiceReverbZone == null)
                _voiceReverbZone = _reverbZoneObject.AddComponent<AudioReverbZone>();
        }

        private void EnsureFemaleCharaRef()
        {
            if (CanonPlugins.Shared.IndependentActorFilter.IsIndependent(_femaleChara)) _femaleChara = null;
            if (_femaleChara != null) return;
            if (Time.unscaledTime < _nextFemaleCharaSearchTime) return;
            _nextFemaleCharaSearchTime = Time.unscaledTime + 0.25f;
            var chaControls = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < chaControls.Length; i++)
            {
                var c = chaControls[i];
                if (c == null) continue;
                if (c.sex != 1) continue;
                _femaleChara = c;
                _femaleBaseY = c.transform.position.y;
                _hasFemaleBaseY = true;
                LogInfo($"female ref assigned: name={c.name} pos={c.transform.position}");
                return;
            }
        }

        private bool TryResolveFemaleHeadPosition(out Vector3 position)
        {
            position = Vector3.zero;

            EnsureFemaleCharaRef();
            if (_femaleChara == null)
                return false;

            if (_femaleChara.objHead != null)
            {
                position = _femaleChara.objHead.transform.position;
                return true;
            }

            Transform root = _femaleChara.transform;
            Transform head = FindChildTransformByName(root, "cf_J_Head");
            if (head != null)
            {
                position = head.position;
                return true;
            }

            Transform neck = FindChildTransformByName(root, "cf_J_Neck");
            if (neck != null)
            {
                position = neck.position + Vector3.up * 0.12f;
                return true;
            }

            position = root.position + Vector3.up * 1.35f;
            return true;
        }

        private static Transform FindChildTransformByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t != null && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        private bool SyncReverbZoneToFemale(bool withLog)
        {
            if (_voiceReverbZone == null || _reverbZoneObject == null) return false;
            EnsureFemaleCharaRef();
            if (_femaleChara == null) return false;

            Vector3 femalePos = _femaleChara.transform.position;
            if ((_reverbZoneObject.transform.position - femalePos).sqrMagnitude > 0.00000001f)
                _reverbZoneObject.transform.position = femalePos;

            if (withLog)
            {
                LogInfo(
                    $"[reverb] zone synced to female pos={femalePos} " +
                    $"zoneWorld={_voiceReverbZone.transform.position} roomWorld={_videoRoomRoot?.transform.position}");
            }
            return true;
        }

        private bool SyncVideoAudioSourceToFemale(bool withLog)
        {
            if (_videoRoomAudioSource == null) return false;
            EnsureFemaleCharaRef();
            if (_femaleChara == null) return false;

            Vector3 femalePos = _femaleChara.transform.position;
            if ((_videoRoomAudioSource.transform.position - femalePos).sqrMagnitude > 0.00000001f)
                _videoRoomAudioSource.transform.position = femalePos;

            if (withLog)
            {
                LogInfo(
                    $"[video-audio] source synced to female pos={femalePos} " +
                    $"sourceWorld={_videoRoomAudioSource.transform.position} " +
                    $"roomWorld={_videoRoomRoot?.transform.position}");
            }

            return true;
        }

        private void SyncActiveVoiceSourcesToVideoRoom(bool withLog)
        {
            if (_videoRoomRoot == null)
                return;

            if (Time.unscaledTime >= _nextVideoRoomVoiceSourceScanTime)
            {
                _nextVideoRoomVoiceSourceScanTime = Time.unscaledTime + 0.25f;
                _videoRoomVoiceSources.Clear();
                AudioSource[] allSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
                for (int i = 0; i < allSources.Length; i++)
                {
                    AudioSource source = allSources[i];
                    if (source == null) continue;
                    int id = source.GetInstanceID();
                    bool isVoice;
                    if (_voiceSourceClassifications.TryGetValue(id, out VoiceSourceClassification cached)
                        && ReferenceEquals(cached.Source, source)
                        && Time.unscaledTime < cached.ValidUntil)
                    {
                        isVoice = cached.IsVoice;
                    }
                    else
                    {
                        isVoice = source.name.StartsWith("h_", StringComparison.OrdinalIgnoreCase) ||
                                  HasNamedAncestor(source.transform, "PlayObjectPCM") ||
                                  source.GetComponent("Voice_Component") != null;
                        if (_voiceSourceClassifications.Count >= 256)
                            _voiceSourceClassifications.Clear();
                        _voiceSourceClassifications[id] = new VoiceSourceClassification
                        {
                            Source = source,
                            IsVoice = isVoice,
                            ValidUntil = Time.unscaledTime + 1f
                        };
                    }
                    if (isVoice) _videoRoomVoiceSources.Add(source);
                }
            }

            Vector3 target = _videoRoomRoot.transform.position;
            EnsureFemaleCharaRef();
            if (TryResolveFemaleHeadPosition(out Vector3 headPosition))
                target = headPosition;

            AudioListener listener = _settings.VerboseLog && !_voiceSnapDone
                ? UnityEngine.Object.FindObjectOfType<AudioListener>()
                : null;
            Vector3 listenerPos = listener != null ? listener.transform.position : Vector3.zero;

            int moved = 0;
            for (int i = 0; i < _videoRoomVoiceSources.Count; i++)
            {
                AudioSource s = _videoRoomVoiceSources[i];
                if (s == null || !s.isPlaying)
                    continue;

                if (_settings.VerboseLog && !_voiceSnapDone)
                {
                    float dist = listener != null
                        ? Vector3.Distance(listenerPos, s.transform.position)
                        : float.NaN;
                    string clipName = s.clip != null ? s.clip.name : "(null)";
                    LogInfo(
                        $"[voice-snap] MOVE name={s.name} " +
                        $"spatial={s.spatialBlend:F2} dist={dist:F1} clip={clipName} " +
                        $"path={BuildTransformPath(s.transform)}");
                }

                if (s.transform != null)
                {
                    if ((s.transform.position - target).sqrMagnitude > 0.00000001f)
                        s.transform.position = target;
                    if (s.transform.rotation != Quaternion.identity)
                        s.transform.rotation = Quaternion.identity;
                }
                moved++;
            }

            if (_settings.VerboseLog && !_voiceSnapDone && moved > 0)
            {
                LogInfo(
                    $"[voice-snap] done listener={listenerPos} target={target} " +
                    $"moved={moved}");
                _voiceSnapDone = true;
            }

            // Hシーン終了（移動ソースなし）でスナップをリセット
            if (moved == 0)
                _voiceSnapDone = false;

            if (!withLog && Time.unscaledTime < _nextVoiceSyncLogTime)
                return;
            _nextVoiceSyncLogTime = Time.unscaledTime + 2f;
            if (_settings.VerboseLog)
                LogInfo($"[voice-sync] active voice sources moved={moved} target={target}");
        }

        private static bool HasNamedAncestor(Transform transform, string namePart)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private void LogReverbDiagnostics(string context)
        {
            try
            {
                var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();

                int playing = 0;
                int spatialPlaying = 0;
                int bypassPlaying = 0;
                int insideZonePlaying = 0;
                Vector3 zonePos = _voiceReverbZone != null ? _voiceReverbZone.transform.position : Vector3.zero;
                float zoneMax = _voiceReverbZone != null ? _voiceReverbZone.maxDistance : 0f;

                var sample = new List<string>(5);
                for (int i = 0; i < sources.Length; i++)
                {
                    var s = sources[i];
                    if (s == null || !s.isPlaying) continue;
                    playing++;

                    bool isSpatial = s.spatialBlend > 0.01f;
                    if (isSpatial) spatialPlaying++;
                    if (s.bypassReverbZones) bypassPlaying++;

                    float distToZone = _voiceReverbZone != null
                        ? Vector3.Distance(s.transform.position, zonePos)
                        : float.NaN;
                    if (_voiceReverbZone != null && distToZone <= zoneMax)
                        insideZonePlaying++;

                    if (sample.Count < 5)
                    {
                        sample.Add(
                            $"name={s.name} spatial={s.spatialBlend:F2} bypass={s.bypassReverbZones} " +
                            $"mix={s.reverbZoneMix:F2} dist={distToZone:F2}");
                    }
                }

                string listenerText = "none";
                if (listener != null)
                {
                    float d = _voiceReverbZone != null
                        ? Vector3.Distance(listener.transform.position, zonePos)
                        : float.NaN;
                    listenerText = $"pos={listener.transform.position} distToZone={d:F2} zoneMax={zoneMax:F2}";
                }

                LogInfo(
                    $"[reverb] diag({context}) listener={listenerText} " +
                    $"sourcesAll={sources.Length} playing={playing} spatialPlaying={spatialPlaying} " +
                    $"insideZonePlaying={insideZonePlaying} bypassPlaying={bypassPlaying}");

                for (int i = 0; i < sample.Count; i++)
                    LogInfo($"[reverb] src[{i}] {sample[i]}");

                if (_voiceReverbZone != null && listener != null)
                {
                    float listenerDist = Vector3.Distance(listener.transform.position, zonePos);
                    if (listenerDist > zoneMax)
                        LogWarn(
                            $"[reverb] listener is outside reverb zone: dist={listenerDist:F2} > max={zoneMax:F2}");
                }

                if (playing > 0 && spatialPlaying == 0)
                    LogWarn("[reverb] no spatial playing sources (spatialBlend<=0), reverb effect will be hard to hear");
            }
            catch (Exception ex)
            {
                LogWarn($"[reverb] diagnostics failed: {ex.Message}");
            }
        }

        private AudioReverbPreset ParseReverbPreset(string rawPresetName)
        {
            string name = rawPresetName?.Trim();
            if (!string.IsNullOrEmpty(name) &&
                Enum.TryParse(name, true, out AudioReverbPreset preset))
                return preset;

            LogWarn($"invalid VoiceReverbPreset='{rawPresetName}', fallback='Cave'");
            return AudioReverbPreset.Cave;
        }

        private void DestroyVideoRoom()
        {
            _videoRoomVoiceSources.Clear();
            _voiceSourceClassifications.Clear();
            _nextVideoRoomVoiceSourceScanTime = 0f;
            StopUiCapture("video room destroyed");
            _editMode = false;
            ClearPendingVideoStarts();
            if (_gizmo != null)
                _gizmo.DragStateChanged -= NotifyGizmoDragState;
            _gizmo = null; // _videoRoomRoot 縺ｨ荳邱偵↓遐ｴ譽・＆繧後ｋ
            _voiceReverbZone = null;
            _reverbZoneObject = null;
            DisposeVideoBindings();
            _mainVideoPlayer = null;
            _videoRoomAudioSource = null;
            _femaleChara = null;
            _nextFemaleCharaSearchTime = 0f;
            _hasFemaleBaseY = false;
            _femaleBaseY = 0f;
            DestroySolidRoomColorResources();
            DestroyImageSlideshowResources();
            DestroyOverlayQuad();
            _videoBindings.Clear();
            DestroyWebCamTextures();
            _roomResourceScope?.Dispose();
            _roomResourceScope = null;
            _videoRoomRoot = null;
        }

        private void DisposeVideoBindings()
        {
            foreach (VideoBinding binding in _videoBindings.Values)
            {
                VideoPlayer player = binding?.Player;
                if (player == null) continue;
                if (binding.PreparedHandler != null) player.prepareCompleted -= binding.PreparedHandler;
                if (binding.LoopPointHandler != null) player.loopPointReached -= binding.LoopPointHandler;
                if (binding.ErrorHandler != null) player.errorReceived -= binding.ErrorHandler;
                try { player.Stop(); } catch { }
                player.targetTexture = null;
            }
        }
    }
}
