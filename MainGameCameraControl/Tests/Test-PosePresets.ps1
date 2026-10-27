# Runs the actual capture/resolve methods with an injected pose and camera snapshot.
# Unity camera acquisition is stubbed; this is not an in-game camera test.
param([string]$SettingsPath)
$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path $PSScriptRoot -Parent
$pose = Get-Content (Join-Path $pluginRoot 'Plugin.PosePresets.cs') -Raw -Encoding UTF8
$start = $pose.IndexOf('        private static CameraPoseOverride FindCameraPoseOverride')
$end = $pose.IndexOf('        private CameraPreset BuildCurrentPresetSnapshot')
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production capture/resolve methods' }
$methods = $pose.Substring($start, $end - $start)
# Windows PowerShell's compiler predates inline out declarations. Syntax-only conversion.
$methods = $methods.Replace('if (!TryGetCurrentPoseInfo(out string key, out _))',
    'string key, unused; if (!TryGetCurrentPoseInfo(out key, out unused))')
$settings = Get-Content (Join-Path $pluginRoot 'PluginSettings.cs') -Raw -Encoding UTF8
$fixture = @'
namespace MainGameCameraControl {
public sealed class PoseRegressionProbe {
    CameraPreset snapshot;
    string currentKey = "test:1";
    bool TryGetCurrentPoseInfo(out string key, out string name) { key = currentKey; name = key; return true; }
    CameraPreset BuildCurrentPresetSnapshot(string name) { return snapshot; }
    void LogInfo(string message) { }
    struct VectorStub { public string ToString(string format) { return "stub"; } }
    static VectorStub ToVector3(float[] values) { return new VectorStub(); }
    static bool Valid(float[] values) { return values != null && values.Length >= 3; }
    static bool PoseHasStoredOffset(CameraPoseOverride p) {
        return Valid(p.CameraOffsetBonePos) || Valid(p.CameraOffsetLocal) || Valid(p.CameraOffsetWorld);
    }
    static bool PresetHasStoredOffset(CameraPreset p) {
        return Valid(p.CameraOffsetBonePos) || Valid(p.CameraOffsetLocal) || Valid(p.CameraOffsetWorld);
    }
    static bool HasPresetCameraPosition(CameraPreset p) { return p.SaveCameraPosition && PresetHasStoredOffset(p); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static T RoundTrip<T>(T value) {
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
        using (var stream = new System.IO.MemoryStream()) {
            serializer.WriteObject(stream, value);
            stream.Position = 0;
            return (T)serializer.ReadObject(stream);
        }
    }
    public static string Run(string settingsPath) {
        var p = new CameraPreset { Name = "test", UsePoseOverrides = true, UseBoneLink = true,
            BoneName = "head", SaveCameraPosition = true, UseRelativeDirection = true,
            RelativeDirLocal = new float[] {0,0,1}, RelativeDistance = 1,
            CameraOffsetBonePos = new float[] {0,0,1} };
        var probe = new PoseRegressionProbe();
        probe.snapshot = new CameraPreset { UseBoneLink = true, BoneName = "head", SaveCameraPosition = true,
            UseRelativeDirection = true, RelativeDirLocal = new float[] {1,0,0}, RelativeDistance = 2,
            RelativeFromLookAt = true, RollWithBone = true, FrameBoneName = "hips",
            CameraOffsetBonePos = new float[] {2,0,0}, Fov = 45 };
        Check(probe.CapturePoseOverrideToPreset(p, "test:1", "first"), "capture failed");
        probe.snapshot.RelativeDirLocal[0] = 9;
        p = RoundTrip(p);
        var loaded = probe.ResolveCameraPresetForCurrentPose(p);
        Check(loaded.RelativeDistance == 2 && loaded.RelativeDirLocal[0] == 1, "saved direction/distance lost");
        Check(loaded.RelativeFromLookAt && loaded.RollWithBone && loaded.FrameBoneName == "hips", "frame flags lost");
        Check(loaded.CameraOffsetBonePos[0] == 2 && loaded.Fov == 45, "position/FOV lost");
        loaded.RelativeDirLocal[0] = 7;
        Check(p.PoseOverrides[0].RelativeDirLocal[0] == 1 && p.RelativeDistance == 1, "resolution mutated stored data");
        probe.snapshot.RelativeDirLocal[0] = 1;
        probe.snapshot.RelativeDistance = 3;
        probe.CapturePoseOverrideToPreset(p, "test:1", "updated");
        Check(p.PoseOverrides.Length == 1 && probe.ResolveCameraPresetForCurrentPose(p).RelativeDistance == 3, "overwrite failed");
        probe.snapshot.RelativeDistance = 4;
        probe.CapturePoseOverrideToPreset(p, "test:2", "second");
        probe.currentKey = "test:2";
        Check(probe.ResolveCameraPresetForCurrentPose(p).RelativeDistance == 4, "second pose failed");
        probe.currentKey = "test:1";
        Check(probe.ResolveCameraPresetForCurrentPose(p).RelativeDistance == 3, "first pose overwritten by second");
        probe.snapshot.UseRelativeDirection = false;
        probe.snapshot.RelativeDirLocal = null;
        probe.snapshot.RelativeDistance = 0;
        probe.CapturePoseOverrideToPreset(p, "test:1", "off");
        p = RoundTrip(p);
        loaded = probe.ResolveCameraPresetForCurrentPose(p);
        Check(!loaded.UseRelativeDirection && loaded.RelativeDirLocal == null, "explicit OFF inherited base");
        Check(loaded.CameraOffsetBonePos[0] == 2, "explicit OFF lost saved position");
        probe.snapshot.UseBoneLink = false;
        probe.CapturePoseOverrideToPreset(p, "test:1", "normal");
        Check(!probe.ResolveCameraPresetForCurrentPose(p).UseBoneLink, "normal mode lost");
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(CameraPoseOverride));
        using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Key\":\"test:1\",\"UseBoneLink\":true}"))) {
            p.PoseOverrides = new CameraPoseOverride[] { (CameraPoseOverride)serializer.ReadObject(stream) };
        }
        p = RoundTrip(p);
        Check(!p.PoseOverrides[0].UseRelativeDirection.HasValue, "legacy data became explicit OFF");
        Check(probe.ResolveCameraPresetForCurrentPose(p).RelativeDistance == 1, "legacy fallback lost");
        probe.currentKey = "unregistered";
        Check(object.ReferenceEquals(p, probe.ResolveCameraPresetForCurrentPose(p)), "unregistered pose changed base");
        if (!string.IsNullOrEmpty(settingsPath)) {
            var settingsSerializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PluginSettings));
            PluginSettings stored;
            using (var stream = System.IO.File.OpenRead(settingsPath)) { stored = (PluginSettings)settingsSerializer.ReadObject(stream); }
            var copy = RoundTrip(stored);
            Check(copy.Presets.Count == stored.Presets.Count, "existing preset count changed");
            for (int i = 0; i < stored.Presets.Count; i++) {
                var before = stored.Presets[i].PoseOverrides;
                var after = copy.Presets[i].PoseOverrides;
                Check((before == null ? 0 : before.Length) == (after == null ? 0 : after.Length), "existing pose count changed");
                if (before == null) continue;
                for (int j = 0; j < before.Length; j++) {
                    Check(before[j].UseRelativeDirection == after[j].UseRelativeDirection, "existing inheritance changed");
                }
            }
        }
        return "PASS: capture, overwrite, pose switching, JSON reload, explicit OFF, normal mode, legacy fallback, clone isolation, existing settings";
    }
'@
Add-Type -TypeDefinition ($settings + "`n" + $fixture + $methods + "`n}}") -ReferencedAssemblies System.Runtime.Serialization,System.Xml
[MainGameCameraControl.PoseRegressionProbe]::Run($SettingsPath)
