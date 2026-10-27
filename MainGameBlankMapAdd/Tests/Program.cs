using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using MainGameBlankMapAdd.Application;
using MainGameBlankMapAdd.Bootstrap;
using MainGameBlankMapAdd.Diagnostics;
using MainGameBlankMapAdd.Settings;
using MainGameBlankMapAdd.Http;
using MainGameBlankMapAdd.Media;
using MainGameBlankMapAdd.Media.Playlist;
using MainGameBlankMapAdd.Media.Images;
using MainGameBlankMapAdd.Media.WebCam;
using MainGameBlankMapAdd.Overlay;
using MainGameBlankMapAdd.Profiles;

namespace MainGameBlankMapAdd.Characterization
{
    internal static class Program
    {
        private static int _checks;

        private static int Main(string[] args)
        {
            try
            {
                string root = args.Length > 0
                    ? Path.GetFullPath(args[0])
                    : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));

                VerifyPublicContracts(root);
                VerifyImpactBoostRouting();
                VerifyArchitectureContracts(root);
                VerifySettingsDocumentContract();
                VerifyLegacySettingsMigration();
                VerifyRoomProfileContract();
                VerifyRoomProfileSelection();
                VerifyPluginPaths();
                VerifyLogRotation();
                VerifySettingsChangePlanning();
                VerifyHttpCommandParsing();
                VerifyMainThreadDispatcher();
                VerifyVideoPathRules();
                VerifyFolderPlaylist();
                VerifySlideshowCatalog();
                SlideshowDateFolderTests.Run();
                SlideshowIoTests.Run();
                SlideshowUvMeshTests.Run();
                VerifyOverlayRules();
                VerifyWebCamCatalog();
                VerifyWebCamPathRules();

                Console.WriteLine($"PASS characterization checks={_checks}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex);
                return 1;
            }
        }

        private static void VerifyImpactBoostRouting()
        {
            bool normal = false, vr = false;
            Action<bool> setNormal = value =>
            {
                normal = value;
                if (normal && vr) throw new Exception("ImpactBoost detectors overlapped");
            };
            Action<bool> setVr = value =>
            {
                vr = value;
                if (normal && vr) throw new Exception("ImpactBoost detectors overlapped");
            };
            foreach (bool enabled in new[] { true, false, true, false })
                foreach (bool mode in new[] { false, true, false })
                {
                    ImpactBoostRouting.Apply(enabled, mode, setNormal, setVr);
                    if (normal != (enabled && !mode) || vr != (enabled && mode))
                        throw new Exception("ImpactBoost mode routing mismatch");
                    _checks++;
                }
        }

        private static void VerifyPublicContracts(string root)
        {
            string plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"), Encoding.UTF8);
            string api = File.ReadAllText(Path.Combine(root, "Plugin.Api.cs"), Encoding.UTF8);

            RequireContains(plugin, "com.kks.maingameblankmapadd", "GUID");
            RequireContains(plugin, "MainGameBlankMapAdd", "assembly/plugin name");
            RequireContains(plugin, "KeyCode.P", "Ctrl+P contract");
            RequireContains(plugin, "KeyCode.R", "Ctrl+R contract");
            RequireContains(plugin, "KeyCode.D", "Ctrl+D contract");
            RequireContains(api, "public static event Action<string> OnVideoLoaded", "OnVideoLoaded API");
            RequireContains(api, "public static event Action<string> OnVideoStarted", "OnVideoStarted API");
            RequireContains(api, "public static event Action<string> OnVideoEnded", "OnVideoEnded API");
            RequireContains(api, "public static event Action OnSettingsApplied", "OnSettingsApplied API");
            RequireContains(api, "public static bool LoadVideo", "LoadVideo API");
            RequireContains(api, "public static string GetCurrentVideoPath", "GetCurrentVideoPath API");
        }

        private static void VerifyArchitectureContracts(string root)
        {
            string plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"), Encoding.UTF8);
            string hooks = File.ReadAllText(Path.Combine(root, "Plugin.MapHooks.cs"), Encoding.UTF8);
            string room = File.ReadAllText(Path.Combine(root, "Plugin.VideoRoom.cs"), Encoding.UTF8);
            string integrationFacade = File.ReadAllText(Path.Combine(root, "Plugin.ExternalBridge.cs"), Encoding.UTF8);
            string integrationRegistry = File.ReadAllText(Path.Combine(root, "Integrations", "IntegrationRegistry.cs"), Encoding.UTF8);
            string astralController = File.ReadAllText(Path.Combine(root, "Room", "AstralRoomController.cs"), Encoding.UTF8);
            string uiCaptureFacade = File.ReadAllText(Path.Combine(root, "Plugin.UiCapture.cs"), Encoding.UTF8);
            string uiCaptureController = File.ReadAllText(Path.Combine(root, "UI", "UiInputCaptureController.cs"), Encoding.UTF8);
            string uiResources = File.ReadAllText(Path.Combine(root, "UI", "UiResourceScope.cs"), Encoding.UTF8);
            string compatibilityResolver = File.ReadAllText(Path.Combine(root, "Compatibility", "PluginCompatibilityResolver.cs"), Encoding.UTF8);
            string audioDiagnostics = File.ReadAllText(Path.Combine(root, "Plugin.AudioDiagnostics.cs"), Encoding.UTF8);
            string configManager = File.ReadAllText(Path.Combine(root, "Plugin.ConfigManager.cs"), Encoding.UTF8);
            string webCamFacade = File.ReadAllText(Path.Combine(root, "Plugin.WebCam.cs"), Encoding.UTF8);
            string webCamSessions = File.ReadAllText(Path.Combine(root, "Media", "WebCam", "WebCamSessionService.cs"), Encoding.UTF8);
            string overlayPlugin = File.ReadAllText(Path.Combine(root, "Plugin.SubCameraOverlay.cs"), Encoding.UTF8);
            string slideshowState = File.ReadAllText(Path.Combine(root, "Media", "Images", "SlideshowRuntimeState.cs"), Encoding.UTF8);
            string overlayState = File.ReadAllText(Path.Combine(root, "Overlay", "OverlayRuntimeState.cs"), Encoding.UTF8);
            string videoState = File.ReadAllText(Path.Combine(root, "Media", "Video", "VideoPlaybackRuntimeState.cs"), Encoding.UTF8);
            string playbackUiState = File.ReadAllText(Path.Combine(root, "UI", "PlaybackUiState.cs"), Encoding.UTF8);

            RequireContains(hooks, "_mapSession.Apply(map, _settings)", "map session delegation");
            Require(hooks.IndexOf("GetComponentsInChildren", StringComparison.Ordinal) < 0, "map hooks do not own blankify traversal");
            Require(plugin.IndexOf("_videoTextures", StringComparison.Ordinal) < 0, "plugin no longer owns render texture list");
            Require(plugin.IndexOf("_generatedMeshes", StringComparison.Ordinal) < 0, "plugin no longer owns generated mesh list");
            RequireContains(room, "DisposeVideoBindings();", "video event handlers disposed");
            RequireContains(room, "_roomResourceScope?.Dispose();", "room resources disposed through scope");
            Require(integrationFacade.IndexOf("Type.GetType", StringComparison.Ordinal) < 0, "plugin integration facade has no reflection");
            RequireContains(integrationRegistry, "internal sealed class IntegrationRegistry", "integration registry extracted");
            RequireContains(integrationRegistry, "MainGameBeatSyncSpeed.Plugin", "BeatSync adapter retained");
            RequireContains(integrationRegistry, "com.kks.maingame.voicefaceeventbridge", "VoiceFace adapter retained");
            Require(plugin.IndexOf("_astralModeSuppressed", StringComparison.Ordinal) < 0, "plugin no longer owns astral suppression state");
            RequireContains(astralController, "internal sealed class AstralRoomController", "astral lifecycle owner");
            RequireContains(astralController, "public void Dispose()", "astral lifecycle disposal");
            Require(uiCaptureFacade.IndexOf("_hasPrevCtrlCamera", StringComparison.Ordinal) < 0, "plugin facade no longer owns camera capture state");
            RequireContains(uiCaptureController, "internal sealed class UiInputCaptureController", "UI input capture state owner");
            RequireContains(uiCaptureController, "public void Dispose()", "UI input capture cleanup");
            RequireContains(uiResources, "internal sealed class UiResourceScope", "UI resource owner");
            RequireContains(uiResources, "Object.Destroy(_blockerRoot)", "UI blocker cleanup");
            RequireContains(uiResources, "Object.Destroy(_blackTexture)", "fade texture cleanup");
            Require(plugin.IndexOf("internal static Plugin Instance", StringComparison.Ordinal) < 0, "plugin has no static runtime owner");
            RequireContains(compatibilityResolver, "Chainloader.PluginInfos.TryGetValue", "legacy API resolves through BepInEx boundary");
            Require(compatibilityResolver.IndexOf("static Plugin _", StringComparison.Ordinal) < 0, "compatibility resolver does not cache plugin state");
            RequireContains(audioDiagnostics, "if (_settings == null || !_settings.EnableAudioDiagnosticsLog)", "all audio snapshots honor diagnostics setting");
            RequireContains(configManager, "\"EnableAudioDiagnosticsLog\"", "audio diagnostics config key retained");
            RequireContains(plugin, "TryLogAudioDiagnosticsTick();", "audio diagnostics tick connected");
            Require(webCamFacade.IndexOf("Dictionary<", StringComparison.Ordinal) < 0, "webcam facade does not own session registry");
            RequireContains(webCamSessions, "internal sealed class WebCamSessionService", "webcam session owner");
            RequireContains(webCamSessions, "public void Dispose()", "webcam session cleanup");
            Require(overlayPlugin.IndexOf("Type.GetType", StringComparison.Ordinal) < 0, "subcamera overlay has no reflection");
            RequireContains(integrationRegistry, "TryGetSubCameraRenderTexture", "subcamera render integration boundary");
            RequireContains(integrationRegistry, "TryGetOverlayBeatSnapshot", "overlay beat integration boundary");
            RequireContains(slideshowState, "internal sealed class SlideshowRuntimeState", "slideshow runtime state owner");
            RequireContains(overlayState, "internal sealed class OverlayRuntimeState", "overlay runtime state owner");
            RequireContains(overlayPlugin, "private readonly OverlayRuntimeState _overlayState", "overlay facade delegates state");
            RequireContains(videoState, "internal sealed class VideoPlaybackRuntimeState", "video playback runtime state owner");
            RequireContains(videoState, "PreparedHandler", "video prepared handler ownership");
            RequireContains(videoState, "ErrorHandler", "video error handler ownership");
            RequireContains(playbackUiState, "internal sealed class PlaybackUiState", "playback UI state owner");
            RequireContains(plugin, "private readonly PlaybackUiState _playbackUi", "plugin playback UI compatibility facade");
        }

        private static void VerifySettingsDocumentContract()
        {
            Type type = typeof(PluginSettings);
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            int serializedFields = fields.Count(f => f.GetCustomAttribute<DataMemberAttribute>() != null);
            Equal(97, serializedFields, "PluginSettings DataMember count");

            var source = new PluginSettings
            {
                AddedMapNo = 910,
                AddedMapName = "video_allpose_910",
                AddedDisplayName = "Video AllPose Room",
                HttpPort = 55982,
                FolderPlayPath = @"H:\video room\music",
                ImageSlideshowPlayMode = "Queue",
                OverlayOpacity = 0.42f
            };

            PluginSettings roundTrip = RoundTrip(source);
            Equal(source.AddedMapNo, roundTrip.AddedMapNo, "AddedMapNo roundtrip");
            Equal(source.AddedMapName, roundTrip.AddedMapName, "AddedMapName roundtrip");
            Equal(source.AddedDisplayName, roundTrip.AddedDisplayName, "AddedDisplayName roundtrip");
            Equal(source.HttpPort, roundTrip.HttpPort, "HttpPort roundtrip");
            Equal(source.FolderPlayPath, roundTrip.FolderPlayPath, "FolderPlayPath roundtrip");
            Equal(source.ImageSlideshowPlayMode, roundTrip.ImageSlideshowPlayMode, "slideshow mode roundtrip");
            Equal(source.OverlayOpacity, roundTrip.OverlayOpacity, "overlay opacity roundtrip");
        }

        private static void VerifyLegacySettingsMigration()
        {
            const string legacy = "{\"AddedMapNo\":900,\"AddedThumbnailID\":0,\"RoomWidth\":0,\"RoomDepth\":0,\"RoomHeight\":0,\"HttpPort\":0,\"CubeFaceTileCount\":3}";
            PluginSettings migrated = Deserialize<PluginSettings>(legacy);

            Equal(5682352, migrated.AddedMapNo, "legacy map id migration");
            Equal(9000, migrated.AddedThumbnailID, "legacy thumbnail id migration");
            Equal(12f, migrated.RoomWidth, "legacy room width migration");
            Equal(12f, migrated.RoomDepth, "legacy room depth migration");
            Equal(6f, migrated.RoomHeight, "legacy room height migration");
            Equal(55782, migrated.HttpPort, "legacy HTTP port migration");
            Equal(1, migrated.CubeFaceTileCount, "legacy tile count migration");
        }

        private static void VerifyRoomProfileContract()
        {
            var source = new RoomLayoutProfile
            {
                Scale = 99f,
                OffsetX = -99f,
                OffsetY = 99f,
                RotationY = 725f,
                HasAudioGain = true,
                AudioGain = 99f,
                HasBeatSync = true,
                BeatBpm = 5000
            };
            source.Normalize();

            Equal(4f, source.Scale, "profile scale clamp");
            Equal(-20f, source.OffsetX, "profile X clamp");
            Equal(10f, source.OffsetY, "profile Y clamp");
            Equal(5f, source.RotationY, "profile rotation normalize");
            Equal(6f, source.AudioGain, "profile gain clamp");
            Equal(999, source.BeatBpm, "profile BPM clamp");

            var repository = new RoomLayoutProfileRepository();
            repository.SetFolder("folder", source);
            repository.SetVideo("video", new RoomLayoutProfile { Scale = 2f });
            Require(repository.TryGetFolder("folder", out RoomLayoutProfile folder), "folder profile lookup");
            Require(repository.TryGetVideo("video", out RoomLayoutProfile video), "video profile lookup");
            Equal(4f, folder.Scale, "folder profile stored clone");
            Equal(2f, video.Scale, "video profile stored clone");
        }

        private static void VerifyRoomProfileSelection()
        {
            var video = new RoomLayoutProfile { HasRoomLayout = true };
            var folder = new RoomLayoutProfile { HasRoomLayout = true, HasAudioGain = true };
            var baseline = new RoomLayoutProfile { HasRoomLayout = true, HasAudioGain = true, HasBeatSync = true };

            RoomProfileSelection layout = RoomProfileSelector.SelectRoomLayout(video, "video-key", folder, "folder-key", baseline);
            Require(ReferenceEquals(video, layout.Profile), "video room layout has highest precedence");
            Equal("video", layout.ScopeName, "video room layout scope");
            Equal(" key=video-key", layout.KeyLogSuffix, "video room layout key suffix");

            video.HasRoomLayout = false;
            layout = RoomProfileSelector.SelectRoomLayout(video, "video-key", folder, "folder-key", baseline);
            Require(ReferenceEquals(folder, layout.Profile), "folder room layout fallback");

            folder.HasRoomLayout = false;
            layout = RoomProfileSelector.SelectRoomLayout(video, "video-key", folder, "folder-key", baseline);
            Require(ReferenceEquals(baseline, layout.Profile), "baseline room layout fallback");
            Equal("folder-baseline", layout.ScopeName, "baseline scope name");
            Equal(string.Empty, layout.KeyLogSuffix, "baseline has no key suffix");

            RoomProfileSelection gain = RoomProfileSelector.SelectAudioGain(video, "video-key", folder, "folder-key");
            Require(ReferenceEquals(folder, gain.Profile), "audio gain uses folder when video section absent");
            folder.HasAudioGain = false;
            gain = RoomProfileSelector.SelectAudioGain(video, "video-key", folder, "folder-key");
            Require(!gain.HasValue, "audio gain does not fall back to baseline");

            RoomProfileSelection beat = RoomProfileSelector.SelectBeatSync(video, "video-key", folder, "folder-key", baseline);
            Require(ReferenceEquals(baseline, beat.Profile), "beat sync baseline fallback");
        }

        private static void VerifyPluginPaths()
        {
            string root = Path.Combine(Path.GetTempPath(), "blank-map-paths");
            PluginPaths paths = PluginPaths.Create(Path.Combine(root, "MainGameBlankMapAdd.dll"), "ignored");
            Equal(Path.GetFullPath(root), paths.PluginDirectory, "plugin directory path");
            Equal(Path.Combine(root, "_logs", "info.txt"), paths.LogFile, "log path");
            Equal(Path.Combine(root, "MapAddSettings.json"), paths.SettingsFile, "settings path");
            Equal(Path.Combine(root, "RoomLayoutProfiles.json"), paths.RoomProfilesFile, "profile path");
        }

        private static void VerifyLogRotation()
        {
            string root = Path.Combine(Path.GetTempPath(), "MainGameBlankMapAdd.Characterization", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "info.txt");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(path, "previous session");
                File.WriteAllText(path + ".1", "previous rotation");
                using (var sink = new RotatingFileLogSink(path, 1024L, 2))
                {
                    Require(File.ReadAllText(path) == string.Empty, "startup clears previous log");
                    Require(!File.Exists(path + ".1"), "startup clears previous rotation");
                    for (int i = 0; i < 40; i++)
                        sink.AppendLine(new string('x', 80) + i);
                }

                Require(File.Exists(path), "active rotating log exists");
                Require(File.Exists(path + ".1"), "first rotating log generation exists");
                Require(new FileInfo(path).Length <= 1100L, "active rotating log bounded");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void VerifySettingsChangePlanning()
        {
            var baseline = RoundTrip(new PluginSettings());
            SettingsSnapshot before = SettingsSnapshot.Capture(baseline);

            var reverb = RoundTrip(baseline);
            reverb.VoiceReverbPreset = "Cave";
            SettingsChangePlan reverbPlan = SettingsChangePlanner.Create(before, SettingsSnapshot.Capture(reverb));
            Require(reverbPlan.HasChanges, "reverb plan detects changes");
            Require(reverbPlan.IsReverbOnly, "reverb plan classified as reverb only fields=" + string.Join(",", reverbPlan.ChangedFields));
            Require(!reverbPlan.RequiresRoomRebuild, "reverb plan avoids room rebuild");

            var video = RoundTrip(baseline);
            video.VideoPath = "sample.mp4";
            SettingsChangePlan videoPlan = SettingsChangePlanner.Create(before, SettingsSnapshot.Capture(video));
            Require(videoPlan.RequiresRoomRebuild, "video path requires room rebuild");
            Require(videoPlan.ChangedFields.Contains(nameof(PluginSettings.VideoPath)), "video plan reports field");

            var diagnostics = RoundTrip(baseline);
            diagnostics.EnableAudioDiagnosticsLog = true;
            SettingsChangePlan diagnosticsPlan = SettingsChangePlanner.Create(before, SettingsSnapshot.Capture(diagnostics));
            Require(!diagnosticsPlan.RequiresRoomRebuild, "diagnostics toggle avoids room rebuild");
            Require(!diagnosticsPlan.IsReverbOnly, "diagnostics toggle is not classified as reverb");

            var folders = RoundTrip(baseline);
            folders.FolderPlayPaths.Add("folder-a");
            SettingsChangePlan folderPlan = SettingsChangePlanner.Create(before, SettingsSnapshot.Capture(folders));
            Require(folderPlan.RequiresRoomRebuild, "folder list requires room rebuild");
        }

        private static void VerifyHttpCommandParsing()
        {
            PlaybackHttpCommand index = HttpCommandParser.ParsePlaybackCommand("{\"index\":12}");
            Equal(12, index.Index, "HTTP index command");
            Equal(null, index.Filename, "HTTP index command filename empty");

            PlaybackHttpCommand file = HttpCommandParser.ParsePlaybackCommand("{\"filename\":\"movie.mp4\"}");
            Equal(null, file.Index, "HTTP filename command index empty");
            Equal("movie.mp4", file.Filename, "HTTP filename command");
        }

        private static void VerifyMainThreadDispatcher()
        {
            using (var dispatcher = new MainThreadDispatcher())
            {
                Require(dispatcher.IsMainThread, "dispatcher captures owner thread");
                Require(dispatcher.TryInvoke(() => 7, 100, out int direct, out string directError), "dispatcher direct invoke");
                Equal(7, direct, "dispatcher direct result");
                Equal(string.Empty, directError, "dispatcher direct error empty");

                int backgroundResult = 0;
                string backgroundError = null;
                bool backgroundOk = false;
                using (var started = new ManualResetEventSlim(false))
                using (var completed = new ManualResetEventSlim(false))
                {
                    var thread = new Thread(() =>
                    {
                        started.Set();
                        backgroundOk = dispatcher.TryInvoke(() => 11, 1000, out backgroundResult, out backgroundError);
                        completed.Set();
                    });
                    thread.Start();
                    Require(started.Wait(500), "dispatcher background started");
                    while (!completed.IsSet)
                    {
                        dispatcher.Drain(ex => throw ex);
                        Thread.Sleep(1);
                    }
                    thread.Join();
                }

                Require(backgroundOk, "dispatcher background invoke");
                Equal(11, backgroundResult, "dispatcher background result");
                Equal(string.Empty, backgroundError, "dispatcher background error empty");
            }

            var stopped = new MainThreadDispatcher();
            stopped.Dispose();
            Require(!stopped.Enqueue(() => { }), "dispatcher rejects after dispose");
        }

        private static void VerifyVideoPathRules()
        {
            Equal(@"C:\video\sample.mp4", VideoPathRules.Normalize("  \"C:\\video\\sample.mp4\"  "), "quoted video path normalize");
            Require(VideoPathRules.IsStreamUrl("https://example.test/video"), "HTTPS stream detected");
            Require(VideoPathRules.IsStreamUrl("rtsp://camera/live"), "RTSP stream detected");
            Require(!VideoPathRules.IsStreamUrl(@"C:\video\sample.mp4"), "local path is not stream");
        }

        private static void VerifyFolderPlaylist()
        {
            string root = Path.Combine(Path.GetTempPath(), "MainGameBlankMapAdd.Playlist", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "b.mp4"), "b");
                File.WriteAllText(Path.Combine(root, "a.mkv"), "a");
                File.WriteAllText(Path.Combine(root, "ignored.txt"), "x");
                var playlist = new FolderPlaylist(new[] { ".mp4", ".mkv" });
                Require(playlist.ScanIfChanged(root, "Name", true, out string error), "playlist initial scan");
                Equal(string.Empty, error, "playlist scan error empty");
                Equal(2, playlist.Files.Length, "playlist filters extensions");
                Equal("a.mkv", Path.GetFileName(playlist.Files[0]), "playlist name sort");
                Require(!playlist.ScanIfChanged(root, "Name", true, out _), "playlist unchanged folder skips scan");
                Require(playlist.TrySelect(1, out string selected), "playlist select");
                Equal("b.mp4", Path.GetFileName(selected), "playlist selected path");
                playlist.Invalidate();
                Require(playlist.ScanIfChanged(root, "Name", false, out _), "playlist invalidation rescans");
                Equal("b.mp4", Path.GetFileName(playlist.Files[0]), "playlist descending sort");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void VerifySlideshowCatalog()
        {
            string root = Path.Combine(Path.GetTempPath(), "MainGameBlankMapAdd.Slides", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "b.png"), "b");
                File.WriteAllText(Path.Combine(root, "a.jpg"), "a");
                File.WriteAllText(Path.Combine(root, "ignored.txt"), "x");
                string[] files = SlideshowFileCatalog.Enumerate(root, new[] { ".png", ".jpg" }, "Name", true, out string error);
                Equal(string.Empty, error, "slideshow scan error empty");
                Equal(2, files.Length, "slideshow filters files");
                Equal("a.jpg", Path.GetFileName(files[0]), "slideshow name sort");
                Equal("Date", SlideshowFileCatalog.NormalizeSortMode("invalid"), "slideshow invalid sort fallback");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void VerifyOverlayRules()
        {
            Equal("All", OverlaySurfaceRules.Normalize(""), "overlay empty normalize");
            Equal("Front,Floor", OverlaySurfaceRules.Normalize("floor;front"), "overlay face order");
            Equal("tile", OverlaySurfaceRules.NormalizeTileMode("TILE"), "overlay tile normalize");
            Equal("single", OverlaySurfaceRules.NormalizeTileMode("bad"), "overlay tile fallback");
            Equal("None", OverlaySurfaceRules.Serialize(OverlaySurfaceRules.Parse("None")), "overlay none serialize");
        }

        private static void VerifyWebCamCatalog()
        {
            string report = WebCamDeviceCatalog.FormatReport(new[] { "OBS Virtual Camera", "Camera 2" });
            RequireContains(report, "webcam://OBS Virtual Camera", "webcam report first device");
            RequireContains(report, "webcam://Camera 2", "webcam report second device");
            RequireContains(WebCamDeviceCatalog.FormatReport(Array.Empty<string>()), "(デバイスなし)", "webcam empty report");
        }

        private static void VerifyWebCamPathRules()
        {
            Require(WebCamPathRules.IsWebCamUrl("webcam://OBS Virtual Camera"), "webcam URL recognized");
            Require(WebCamPathRules.IsWebCamUrl("WEBCAM://Camera 2"), "webcam URL is case insensitive");
            Equal("OBS Virtual Camera", WebCamPathRules.ExtractDeviceName("webcam://OBS Virtual Camera"), "webcam device extracted");
            Equal(string.Empty, WebCamPathRules.ExtractDeviceName("movie.mp4"), "non-webcam path has no device");
        }

        private static PluginSettings RoundTrip(PluginSettings value)
        {
            var serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (PluginSettings)serializer.ReadObject(stream);
            }
        }

        private static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)serializer.ReadObject(stream);
        }

        private static void RequireContains(string value, string expected, string name)
        {
            Require(value.IndexOf(expected, StringComparison.Ordinal) >= 0, name);
        }

        private static void Require(bool value, string name)
        {
            _checks++;
            if (!value)
                throw new InvalidOperationException("Characterization failed: " + name);
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            _checks++;
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"Characterization failed: {name}; expected={expected}; actual={actual}");
        }
    }
}
