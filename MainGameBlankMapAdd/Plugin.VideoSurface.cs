using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using MainGameBlankMapAdd.Media;
using MainGameBlankMapAdd.Media.Video;
using UnityEngine;
using UnityEngine.Video;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        [DisallowMultipleComponent]
        private sealed class DualMonoFilter : MonoBehaviour
        {
            public bool Enabled = true;
            public float Gain = 1f;

            private void OnAudioFilterRead(float[] data, int channels)
            {
                if (!Enabled || data == null || channels < 2)
                    return;

                float gain = Mathf.Clamp(Gain, 0f, 6f);
                for (int i = 0; i + 1 < data.Length; i += channels)
                {
                    float mono = (data[i] + data[i + 1]) * 0.5f * gain;
                    mono = Mathf.Clamp(mono, -1f, 1f);
                    data[i] = mono;
                    data[i + 1] = mono;
                }
            }
        }

        private void CreateSphereSurface(Transform parent, string videoPath)
        {
            float r = Mathf.Max(0.5f, _settings.SphereRadius);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "VideoSurface_Sphere";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(r, r, r);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) { LogWarn("sphere renderer missing"); return; }
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                LogWarn("sphere mesh missing");
                return;
            }

            // Duplicate built-in mesh and flip orientation when inside view is enabled.
            var meshCopy = Instantiate(meshFilter.sharedMesh);
            ConfigureSphereMesh(meshCopy, _settings.SphereInsideView);
            meshFilter.sharedMesh = meshCopy;
            _roomResourceScope?.Own(meshCopy);

            // Standard shader + Emission 縺ｧ辣ｧ譏弱↓萓晏ｭ倥＠縺ｪ縺・ン繝・が陦ｨ遉ｺ
            var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = "VideoMat_Sphere" };
            _roomResourceScope?.Own(material);
            if (shader.name == "Standard")
            {
                ApplySphereCull(material);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Glossiness", 0f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.white);
            }
            renderer.sharedMaterial = material;

            if (string.IsNullOrWhiteSpace(videoPath))
            {
                LogWarn("video path unresolved for sphere");
                return;
            }

            TryAttachVideo(material, videoPath, "Sphere", withAudio: true);

            // Assign same RT to emission for stable brightness in unlit rooms.
            if (shader.name == "Standard" && material.mainTexture != null)
                material.SetTexture("_EmissionMap", material.mainTexture);

            LogInfo(
                $"sphere created radius={r:F2} insideView={_settings.SphereInsideView} " +
                $"scale={go.transform.localScale} shader={shader.name} meshVerts={meshCopy.vertexCount}");
        }

        private void ApplySphereVisualSettings()
        {
            if (_videoRoomRoot == null || !_settings.UseSphere) return;

            Transform sphereTr = _videoRoomRoot.transform.Find("VideoSurface_Sphere");
            if (sphereTr == null) return;

            float r = Mathf.Max(0.5f, _settings.SphereRadius);
            sphereTr.localScale = new Vector3(r, r, r);

            var mr = sphereTr.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null)
                ApplySphereCull(mr.sharedMaterial);

            float listenerDist = float.NaN;
            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            if (listener != null)
                listenerDist = Vector3.Distance(listener.transform.position, sphereTr.position);

            LogInfo(
                $"sphere updated radius={r:F2} insideView={_settings.SphereInsideView} " +
                $"scale={sphereTr.localScale} listenerDist={listenerDist:F2}");
        }

        private void ApplySphereCull(Material material)
        {
            if (material == null) return;

            // 繝｡繝・す繝･蜷代″縺ｧ蜀・､悶ｒ蛻・ｊ譖ｿ縺医ｋ縺溘ａ縲，ull 縺ｯ Back 蝗ｺ螳壹〒繧医＞縲・            // Unity Built-in: 0=Off, 1=Front, 2=Back
            int cull = 2;
            if (material.HasProperty("_Cull"))
                material.SetInt("_Cull", cull);
            if (material.HasProperty("_CullMode"))
                material.SetInt("_CullMode", cull);
            LogInfo($"sphere cull mode set to Back (insideView={_settings.SphereInsideView})");
        }

        private void ConfigureSphereMesh(Mesh mesh, bool insideView)
        {
            if (mesh == null) return;
            if (!insideView) return;

            var triangles = mesh.triangles;
            var normals = mesh.normals;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int t = triangles[i];
                triangles[i] = triangles[i + 1];
                triangles[i + 1] = t;
            }

            if (normals != null && normals.Length > 0)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = -normals[i];
                mesh.normals = normals;
            }

            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            LogInfo($"sphere mesh orientation updated insideView={insideView}");
        }

        private void CreateVideoSurface(
            Transform parent,
            string surfaceName,
            Vector3 localPos,
            Quaternion localRot,
            Vector2 size,
            string videoPath,
            bool withAudio = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"VideoSurface_{surfaceName}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                LogWarn($"video surface renderer missing: {surfaceName}");
                return;
            }

            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = $"VideoMat_{surfaceName}"
            };
            _roomResourceScope?.Own(material);
            renderer.sharedMaterial = material;

            if (string.IsNullOrWhiteSpace(videoPath))
            {
                LogWarn($"video path unresolved for {surfaceName}, keep static material");
                return;
            }

            TryAttachVideo(material, videoPath, surfaceName, withAudio);
        }

        private string PickVideoPathForSurface(string surfaceKind, string sharedVideoPath)
        {
            if (string.Equals(surfaceKind, "floor", StringComparison.OrdinalIgnoreCase) &&
                _settings.UseFloorVideoOverride)
            {
                return ResolveVideoOverridePath(
                    _settings.FloorOverrideVideoPath,
                    sharedVideoPath,
                    "floor");
            }

            if (string.Equals(surfaceKind, "ceiling", StringComparison.OrdinalIgnoreCase) &&
                _settings.UseCeilingVideoOverride)
            {
                return ResolveVideoOverridePath(
                    _settings.CeilingOverrideVideoPath,
                    sharedVideoPath,
                    "ceiling");
            }

            return sharedVideoPath;
        }

        private string ResolveVideoOverridePath(string configuredPath, string fallbackPath, string surfaceKind)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return fallbackPath;

            string resolved = ResolveVideoPath(configuredPath);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            LogWarn($"{surfaceKind} override unresolved, fallback to shared video");
            return fallbackPath;
        }

        private static bool IsStreamUrl(string path)
        {
            return VideoPathRules.IsStreamUrl(path);
        }

        private string ResolveVideoPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return null;

            string normalized = NormalizeVideoPathInput(configuredPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            // ストリームURL / WebCamはファイル存在チェックをスキップしてそのまま返す
            if (IsStreamUrl(normalized) || IsWebCamUrl(normalized))
            {
                LogInfo($"[stream/webcam] URL detected: {normalized}");
                return normalized;
            }

            try
            {
                if (Path.IsPathRooted(normalized))
                {
                    if (File.Exists(normalized))
                        return ResolveMonoVideoPathIfNeeded(normalized);
                    LogWarn($"video file not found: {normalized}");
                    return null;
                }

                string combined = Path.Combine(_pluginDir, normalized);
                if (File.Exists(combined))
                    return ResolveMonoVideoPathIfNeeded(combined);

                LogWarn($"video file not found: {combined}");
                return null;
            }
            catch (Exception ex)
            {
                LogWarn($"video path parse failed value={configuredPath} normalized={normalized} error={ex.Message}");
                return null;
            }
        }

        private void TryAttachVideo(Material material, string videoPath, string surfaceName, bool withAudio)
        {
            if (material == null || string.IsNullOrWhiteSpace(videoPath))
                return;

            // WebCamTexture ルート
            if (IsWebCamUrl(videoPath))
            {
                string deviceName = ExtractWebCamDeviceName(videoPath);
                TryAttachWebCam(material, deviceName);
                return;
            }

            bool created = false;
            if (!_videoBindings.TryGetValue(videoPath, out var binding) ||
                binding == null ||
                binding.Player == null ||
                binding.Texture == null)
            {
                var host = new GameObject($"VideoSource_{_videoBindings.Count + 1}");
                if (_videoRoomRoot != null)
                    host.transform.SetParent(_videoRoomRoot.transform, false);

                var player = host.AddComponent<VideoPlayer>();
                bool isStream = IsStreamUrl(videoPath);
                player.source = VideoSource.Url;
                player.url = videoPath;
                player.renderMode = VideoRenderMode.RenderTexture;
                player.playOnAwake = false;
                player.waitForFirstFrame = !isStream; // ストリームは最初フレーム待ちをしない
                player.skipOnDrop = true;
                player.isLooping = isStream ? false : (_settings.VideoLoop && !_settings.FolderPlayEnabled);
                player.aspectRatio = VideoAspectRatio.Stretch;
                ConfigureVideoAudio(player, withAudio);

                var rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
                {
                    name = $"VideoRT_{_videoBindings.Count + 1}",
                    useMipMap = false,
                    autoGenerateMips = false
                };
                rt.Create();
                _roomResourceScope?.Own(rt);

                player.targetTexture = rt;
                binding = new VideoBinding
                {
                    Player = player,
                    Texture = rt,
                    RoomGeneration = _roomResourceScope?.Generation ?? Guid.Empty
                };
                _videoBindings[videoPath] = binding;

                // イベントハンドラをbindingに保持して後から解除できるようにする
                string capturedPath = videoPath;
                binding.PreparedHandler = _ => OnVideoPrepared(capturedPath, binding, surfaceName);
                binding.LoopPointHandler = _ =>
                {
                    if (_roomResourceScope?.Owns(binding.RoomGeneration) == true)
                        FireOnVideoEnded(capturedPath);
                };
                binding.ErrorHandler = (_, message) =>
                {
                    if (_roomResourceScope?.Owns(binding.RoomGeneration) == true)
                        LogError($"video error surface={surfaceName} path={videoPath} message={message}");
                };
                player.prepareCompleted += binding.PreparedHandler;
                player.loopPointReached += binding.LoopPointHandler;
                player.errorReceived += binding.ErrorHandler;
                created = true;
            }
            else if (withAudio)
            {
                ConfigureVideoAudio(binding.Player, true);
            }

            if (!binding.Materials.Contains(material))
                binding.Materials.Add(material);

            material.mainTexture = binding.Texture;
            if (material.HasProperty("_EmissionMap"))
                material.SetTexture("_EmissionMap", binding.Texture);
            ApplySquareCropToMaterial(material, binding.Player);

            if (withAudio)
                _mainVideoPlayer = binding.Player;

            if (!created)
            {
                LogInfo($"video source reused surface={surfaceName} path={videoPath}");
                return;
            }

            bool autoPlayOnMapLoad = _settings != null && _settings.AutoPlayOnMapLoad;
            if (IsStreamUrl(videoPath))
            {
                // ストリームは Prepare() が完了しないため再生開始時は直接 Play() する。
                // マップ読み込み時は AutoPlayOnMapLoad=false なら開始しない。
                if (autoPlayOnMapLoad && !IsAstralModeSuppressed && !ExternalMediaBlocksPlayback)
                {
                    binding.Player.Play();
                    LogInfo($"video stream play started surface={surfaceName} path={videoPath} audio={withAudio}");
                    if (withAudio) FireOnVideoStarted(videoPath);
                }
                else
                {
                    string reason = IsAstralModeSuppressed ? "astral-suppressed" : "autoplay-off";
                    ForceHoldVideoAtStart(binding.Player, videoPath, reason);
                    LogInfo($"video stream autoplay skipped reason={reason} surface={surfaceName} path={videoPath}");
                }
            }
            else
            {
                binding.Player.Prepare();
                QueueVideoPlaybackStart(binding.Player, surfaceName, videoPath, autoPlayOnMapLoad);
                LogInfo($"video source created surface={surfaceName} path={videoPath} audio={withAudio}");
            }
        }

        private void OnVideoPrepared(string videoPath, VideoBinding binding, string surfaceName)
        {
            if (binding == null || _roomResourceScope?.Owns(binding.RoomGeneration) != true)
                return;
            ApplySquareCropToBinding(binding);
            if (binding?.Player != null && binding.Player == _mainVideoPlayer)
            {
                ConfigureVideoAudio(binding.Player, true);
            }
            ulong width = binding?.Player != null ? binding.Player.width : 0ul;
            ulong height = binding?.Player != null ? binding.Player.height : 0ul;
            LogInfo($"video prepared surface={surfaceName} path={videoPath} size={width}x{height}");

            // Some codecs/drivers can start playback right after Prepare() unexpectedly.
            // Keep it paused at map-load when autoplay is explicitly disabled.
            if (binding?.Player != null && IsPendingManualStart(binding.Player))
            {
                ForceHoldVideoAtStart(binding.Player, videoPath, "prepared-autoplay-off");
            }

            if (binding?.Player == _mainVideoPlayer)
                FireOnVideoLoaded(videoPath);
        }

        private void ApplySquareCropToBinding(VideoBinding binding)
        {
            if (binding == null || binding.Player == null || binding.Materials == null)
                return;

            for (int i = 0; i < binding.Materials.Count; i++)
            {
                var mat = binding.Materials[i];
                if (mat == null) continue;
                ApplySquareCropToMaterial(mat, binding.Player);
            }
        }

        private static void ApplySquareCropToMaterial(Material material, VideoPlayer player)
        {
            if (material == null) return;

            ComputeSquareCrop(player, out var scale, out var offset);
            material.mainTextureScale = scale;
            material.mainTextureOffset = offset;
            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", scale);
                material.SetTextureOffset("_MainTex", offset);
            }
            if (material.HasProperty("_EmissionMap"))
            {
                material.SetTextureScale("_EmissionMap", scale);
                material.SetTextureOffset("_EmissionMap", offset);
            }
        }

        private static void ComputeSquareCrop(VideoPlayer player, out Vector2 scale, out Vector2 offset)
        {
            scale = Vector2.one;
            offset = Vector2.zero;
            if (player == null) return;

            float w = player.width;
            float h = player.height;
            if (w <= 1f || h <= 1f) return;

            if (w > h)
            {
                float sx = h / w;
                scale = new Vector2(sx, 1f);
                offset = new Vector2((1f - sx) * 0.5f, 0f);
                return;
            }

            if (h > w)
            {
                float sy = w / h;
                scale = new Vector2(1f, sy);
                offset = new Vector2(0f, (1f - sy) * 0.5f);
            }
        }

        private void ConfigureVideoAudio(VideoPlayer player, bool withAudio)
        {
            if (player == null) return;

            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, withAudio);
            bool mute = !withAudio || _settings.MuteVideoAudio;
            float volume = withAudio ? Mathf.Clamp01(_settings.VideoVolume) : 0f;
            ForceDisableDirectAudio(player);

            AudioSource target = EnsureVideoRoomAudioSource();
            if (!withAudio || target == null)
            {
                player.audioOutputMode = VideoAudioOutputMode.None;
                ForceDisableDirectAudio(player);
                LogInfo(
                    $"[video-audio] configure mode=None withAudio={withAudio} " +
                    $"target={(target == null ? "(null)" : FormatAudioSourceName(target))} path={player.url}");
                return;
            }

            ConfigureVideoRoomAudioSource(target);
            EnsureDualMonoFilter(target);
            target.mute = mute;
            target.volume = volume;

            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.SetTargetAudioSource((ushort)0, target);
            ForceDisableDirectAudio(player);
            LogInfo(
                $"[video-audio] configure mode=AudioSource withAudio={withAudio} " +
                $"mute={mute} volume={volume:F3} target={FormatAudioSourceName(target)} path={player.url}");
            if (withAudio)
            {
                LogAudioDiagnosticsSnapshot("configure-video-audio", includeStoppedSources: true);
            }
        }

        private AudioSource EnsureVideoRoomAudioSource()
        {
            if (_videoRoomAudioSource != null)
            {
                ConfigureVideoRoomAudioSource(_videoRoomAudioSource);
                EnsureDualMonoFilter(_videoRoomAudioSource);
                return _videoRoomAudioSource;
            }

            if (_videoRoomRoot == null)
                return null;

            Transform existing = _videoRoomRoot.transform.Find("__VideoRoomAudioSource");
            GameObject host = existing != null
                ? existing.gameObject
                : new GameObject("__VideoRoomAudioSource");

            if (existing == null)
            {
                host.transform.SetParent(_videoRoomRoot.transform, false);
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.identity;
            }

            _videoRoomAudioSource = host.GetComponent<AudioSource>();
            if (_videoRoomAudioSource == null)
                _videoRoomAudioSource = host.AddComponent<AudioSource>();

            ConfigureVideoRoomAudioSource(_videoRoomAudioSource);
            EnsureDualMonoFilter(_videoRoomAudioSource);
            SyncVideoAudioSourceToFemale(true);
            return _videoRoomAudioSource;
        }

        private void EnsureDualMonoFilter(AudioSource source)
        {
            if (source == null)
                return;

            DualMonoFilter filter = source.GetComponent<DualMonoFilter>();
            if (filter == null)
                filter = source.gameObject.AddComponent<DualMonoFilter>();
            filter.Enabled = true;
            filter.Gain = ResolveCurrentVideoAudioGain();
        }

        private float ResolveCurrentVideoAudioGain()
        {
            if (_settings == null)
                return 1f;

            float gain = _settings.VideoAudioGain;
            if (float.IsNaN(gain) || float.IsInfinity(gain) || gain <= 0f)
                gain = 1f;
            return Mathf.Clamp(gain, 0.1f, 6f);
        }

        private void ConfigureVideoRoomAudioSource(AudioSource source)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            // Force true 2D center audio for the video channel.
            source.spatialBlend = 0f;
            source.panStereo = 0f;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.5f;

            float roomMax = Mathf.Max(
                1f,
                Mathf.Max(
                    _settings?.RoomWidth ?? 12f,
                    Mathf.Max(_settings?.RoomDepth ?? 12f, _settings?.RoomHeight ?? 6f)));
            float roomScale = Mathf.Max(0.25f, _playbackRoomScale);
            source.maxDistance = Mathf.Max(6f, roomMax * roomScale * 1.5f);
            float videoReverbMix = ResolveVideoAudioReverbMix();
            bool enableVideoReverb =
                (_settings?.ApplyReverbToVideoAudio ?? false) &&
                videoReverbMix > 0.0001f;
            source.bypassReverbZones = !enableVideoReverb;
            source.reverbZoneMix = enableVideoReverb ? videoReverbMix : 0f;
            source.mute = _settings?.MuteVideoAudio ?? false;
            source.volume = Mathf.Clamp01(_settings?.VideoVolume ?? 0.5f);
            EnsureDualMonoFilter(source);
        }

        private float ResolveVideoAudioReverbMix()
        {
            if (_settings == null || !_settings.EnableVoiceReverb)
                return 0f;
            return MapReverbStrengthToMix(1f);
        }

        private void ApplyRuntimeVideoAudioLevel(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            bool mute = _settings?.MuteVideoAudio ?? false;

            if (_mainVideoPlayer == null)
                return;

            AudioSource target = EnsureVideoRoomAudioSource();
            if (target == null)
            {
                LogWarn("[video-audio] runtime route failed target=null");
                return;
            }

            ConfigureVideoRoomAudioSource(target);
            EnsureDualMonoFilter(target);
            target.volume = clamped;
            target.mute = mute;

            _mainVideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _mainVideoPlayer.SetTargetAudioSource((ushort)0, target);
            ForceDisableDirectAudio(_mainVideoPlayer);
            LogInfo(
                $"[video-audio] runtime mode=AudioSource mute={mute} volume={clamped:F3} gain={ResolveCurrentVideoAudioGain():F3} " +
                $"target={FormatAudioSourceName(target)}");
        }

        private static void ForceDisableDirectAudio(VideoPlayer player)
        {
            if (player == null)
                return;

            int tracks = 1;
            try
            {
                tracks = Mathf.Max(1, (int)player.controlledAudioTrackCount);
            }
            catch
            {
                tracks = 1;
            }

            for (ushort i = 0; i < tracks; i++)
            {
                try { player.SetDirectAudioMute(i, true); } catch { }
                try { player.SetDirectAudioVolume(i, 0f); } catch { }
            }
        }

        private void QueueVideoPlaybackStart(VideoPlayer player, string surfaceName, string videoPath, bool playWhenReady = true)
        {
            if (ExternalMediaBlocksPlayback) return;
            if (player == null) return;
            if (IsAstralModeSuppressed)
            {
                ForceHoldVideoAtStart(player, videoPath, "astral-suppressed");
                LogInfo($"video start blocked by AstralMode surface={surfaceName} path={videoPath}");
                return;
            }

            _pendingVideoStarts.Add(new PendingVideoStart
            {
                Player = player,
                SurfaceName = surfaceName,
                VideoPath = videoPath,
                PlayWhenReady = playWhenReady
            });
            LogInfo($"video start queued surface={surfaceName} path={videoPath} playWhenReady={playWhenReady}");
        }

        private void TryStartPendingVideosIfRoomReady()
        {
            if (ExternalMediaBlocksPlayback) { _pendingVideoStarts.Clear(); return; }
            if (_pendingVideoStarts.Count == 0) return;
            if (IsAstralModeSuppressed)
            {
                for (int i = 0; i < _pendingVideoStarts.Count; i++)
                {
                    PendingVideoStart pending = _pendingVideoStarts[i];
                    if (pending?.Player != null)
                        ForceHoldVideoAtStart(pending.Player, pending.VideoPath, "astral-suppressed-pending");
                }
                _pendingVideoStarts.Clear();
                LogInfo("video pending starts cleared by AstralMode suppression");
                return;
            }

            if (!IsVideoPlaybackReady(out string waitReason))
            {
                if (Time.unscaledTime >= _nextPendingVideoLogTime)
                {
                    _nextPendingVideoLogTime = Time.unscaledTime + 2f;
                    LogInfo($"video start waiting reason={waitReason} pending={_pendingVideoStarts.Count}");
                }
                return;
            }

            _nextPendingVideoLogTime = 0f;

            int started = 0;
            for (int i = _pendingVideoStarts.Count - 1; i >= 0; i--)
            {
                PendingVideoStart pending = _pendingVideoStarts[i];
                if (pending?.Player == null)
                {
                    _pendingVideoStarts.RemoveAt(i);
                    continue;
                }

                if (!pending.Player.isPrepared)
                    continue;

                if (!pending.PlayWhenReady)
                {
                    ForceHoldVideoAtStart(pending.Player, pending.VideoPath, "skip-autoplay");
                    LogInfo($"video autoplay skipped surface={pending.SurfaceName} path={pending.VideoPath}");
                    _pendingVideoStarts.RemoveAt(i);
                    continue;
                }

                pending.Player.Play();
                started++;
                LogInfo($"video playback start surface={pending.SurfaceName} path={pending.VideoPath}");

                if (pending.Player == _mainVideoPlayer)
                    FireOnVideoStarted(pending.VideoPath);
                _pendingVideoStarts.RemoveAt(i);
            }

            if (started == 0 && Time.unscaledTime >= _nextPendingVideoLogTime)
            {
                _nextPendingVideoLogTime = Time.unscaledTime + 2f;
                LogInfo($"video start waiting reason=prepare pending={_pendingVideoStarts.Count}");
            }
        }

        private bool IsVideoPlaybackReady(out string reason)
        {
            if (IsAstralModeSuppressed)
            {
                reason = "astral-suppressed";
                return false;
            }

            if (_videoRoomRoot == null)
            {
                reason = "room-null";
                return false;
            }

            if (LastReservedMap == null)
            {
                reason = "map-null";
                return false;
            }

            if (LastReservedMap.no != _settings.AddedMapNo)
            {
                reason = $"map-no-mismatch({LastReservedMap.no})";
                return false;
            }

            if (LastReservedMap.mapRoot == null)
            {
                reason = "map-root-null";
                return false;
            }

            if (LastReservedMap.isMapLoading)
            {
                reason = "base-map-loading";
                return false;
            }

            bool nowLoading = false;
            bool nowLoadingFade = false;
            try
            {
                nowLoading = Manager.Scene.IsNowLoading;
                nowLoadingFade = Manager.Scene.IsNowLoadingFade;
            }
            catch (Exception ex)
            {
                LogWarn($"video start scene-state access failed: {ex.Message}");
            }

            if (nowLoading)
            {
                reason = "scene-loading";
                return false;
            }

            if (nowLoadingFade)
            {
                reason = "scene-loading-fade";
                return false;
            }

            reason = "ready";
            return true;
        }

        private void ClearPendingVideoStarts()
        {
            _pendingVideoStarts.Clear();
            _nextPendingVideoLogTime = 0f;
        }

        private bool IsPendingManualStart(VideoPlayer player)
        {
            if (player == null) return false;

            for (int i = 0; i < _pendingVideoStarts.Count; i++)
            {
                PendingVideoStart pending = _pendingVideoStarts[i];
                if (pending == null) continue;
                if (!ReferenceEquals(pending.Player, player)) continue;
                if (pending.PlayWhenReady) continue;
                return true;
            }

            return false;
        }

        private void ForceHoldVideoAtStart(VideoPlayer player, string path, string reason)
        {
            if (player == null) return;

            bool wasPlaying = false;
            try
            {
                wasPlaying = player.isPlaying;
            }
            catch
            {
                // no-op
            }

            try
            {
                player.Pause();
            }
            catch
            {
                // no-op
            }

            try
            {
                if (player.canSetTime)
                    player.time = 0d;
            }
            catch
            {
                // no-op
            }

            LogInfo($"video hold reason={reason} path={path} wasPlaying={wasPlaying}");
        }
    }
}
