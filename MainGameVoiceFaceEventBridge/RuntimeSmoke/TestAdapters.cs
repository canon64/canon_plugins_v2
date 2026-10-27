// Only game/VR host adapters are faked. AudioSource, DSP, file decoding, the player,
// detector, settings and both trigger services are the real production code.
using BepInEx.Configuration;
using UnityEngine;

public sealed class ChaControl
{
    public GameObject objBodyBone;
    public AudioSource LipSource;
    private bool _fixed;
    public void SetLipSync(AudioSource source) { LipSource = source; }
    public bool GetMouthFixed() => _fixed;
    public void ChangeMouthFixed(bool value) { _fixed = value; }
}
namespace CanonPlugins.Shared
{
    internal static class BlankRoomGate { internal static bool IsInBlankRoom = true; }
}
namespace MainGameVoiceFaceEventBridge
{
    internal sealed class Plugin
    {
        internal static Plugin Instance;
        internal ExternalVoicePlayer Player;
        internal ChaControl Female = new ChaControl();
        internal void SetImpactCueEnabled(string owner, bool enabled) => Player.SetImpactCueEnabled(owner, enabled);
        internal bool RequestImpactCue(string owner) => Player.RequestImpactCue(owner, Female);
        internal bool RequestPriorityDeepCue(string owner) => Player.RequestPriorityDeepCue(owner, Female);
        internal static void LogImpactCueDiagnostic(string text) => Debug.Log(text);
        internal bool RequestExternalVolumeBoost(float p, float a, float h, float r, float s, float so, float si, PublicApi.EasingShape e)
            => Player.RequestTransientBoost(p, a, h, r, s, so, si, e);
    }
}
namespace MainGameVrHipIkVoiceImpactBoost
{
    internal static class Plugin { internal const string GUID = "com.kks.main.vrhipikvoiceimpactboost"; }
    internal sealed class PluginFileLogger { internal void LogInfo(string text) => Debug.Log(text); }
    internal static class MillimeterDistanceDrawer { internal static void Draw(ConfigEntryBase entry) { } }
    internal sealed class HipIkTrackingPositionSource
    {
        internal Vector3 Hip;
        internal bool TryGetHip(out Vector3 hip, out bool running, out string source)
        { hip = Hip; running = true; source = "simulated_hip"; return true; }
        internal bool TryGetHead(out Vector3 head, out string source)
        { head = Vector3.up; source = "simulated_head"; return true; }
    }
}
namespace MainGameVoiceImpactBoost
{
    internal static class Plugin { internal const string GUID = "com.kks.main.voiceimpactboost"; }
    internal sealed class PluginFileLogger { internal void LogInfo(string text) => Debug.Log(text); }
    internal static class BeatSyncIntensityReader { internal static float Read() => 1; }
}
