using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using MainGameVoiceFaceEventBridge;
using MainGameVoiceFaceEventBridge.Audio;
using UnityEngine;

public sealed partial class RuntimeSmoke : MonoBehaviour
{
    private ExternalVoicePlayer _player;
    private MainGameVoiceFaceEventBridge.Plugin _host;
    private int _checks, _sequenceFinished;
    private bool _done;
    private string _root;
    private const string VrOwner = "com.kks.main.vrhipikvoiceimpactboost";
    private AudioSource Dialogue => Field<AudioSource>("_source");
    private AudioSource Cue => Field<AudioSource>("_cueSource");
    private ImpactCueState State => Field<ImpactCueState>("_cueState");
    private T Field<T>(string name) => (T)typeof(ExternalVoicePlayer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_player);

    private void Awake()
    {
        string root = Environment.GetEnvironmentVariable("IMPACT_CUE_SMOKE_ROOT");
        typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Path.Combine(root, "Player", "ImpactCueSmoke.exe"),
                Path.Combine(root, "SmokeBepInEx"), Path.Combine(root, "Player", "ImpactCueSmoke_Data", "Managed"), new string[0] });
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 120;
        AudioListener.volume = 0; // DSP remains active; this automated run does not play over the user's game.
        gameObject.AddComponent<AudioListener>();
        _root = Environment.GetEnvironmentVariable("IMPACT_CUE_SMOKE_ROOT");
        if (string.IsNullOrEmpty(_root)) { Fail("missing IMPACT_CUE_SMOKE_ROOT"); yield break; }
        Application.logMessageReceived += OnLog;
        _player = new ExternalVoicePlayer(Debug.Log, Debug.LogWarning, Debug.LogError);
        if (Environment.GetEnvironmentVariable("VOICE_REVERB_SMOKE") == "1")
        {
            yield return CheckVoiceReverb();
            yield break;
        }
        if (Environment.GetEnvironmentVariable("VOICE_VOLUME_SMOKE") == "1")
        {
            yield return CheckVoiceVolume();
            yield break;
        }
        _host = new MainGameVoiceFaceEventBridge.Plugin { Player = _player };
        MainGameVoiceFaceEventBridge.Plugin.Instance = _host;
        var cueSettings = new ImpactCueSettings(new ConfigFile(Path.Combine(_root, "smoke-audio.cfg"), false));
        cueSettings.TriggerChancePercent.Value = 100; // Playback lifecycle assertions require deterministic acceptance.
        _player.InitializeImpactCues(Path.Combine(_root, "..", "ImpactCueAudio"), cueSettings);
        _player.SequenceFinished += () => _sequenceFinished++;
        PublicApi.SetImpactCueEnabled(VrOwner, true);
        Check(Field<bool>("_cueReady"), "selected clips preloaded");
        if (Environment.GetEnvironmentVariable("DEEP_CUE_SMOKE") == "1")
        {
            const string deepOwner = "com.kks.maingame.vrdildo";
            PublicApi.SetImpactCueEnabled(deepOwner, true);
            Check(PublicApi.TryRequestPriorityDeepCue(deepOwner) && Cue.isPlaying, "deep cue starts");
            AudioClip firstDeep = Cue.clip;
            yield return new WaitForSecondsRealtime(.1f);
            Check(PublicApi.TryRequestPriorityDeepCue(deepOwner) && Cue.isPlaying && Cue.clip != firstDeep,
                "new deep hit replaces playing deep cue");
            string deepDialoguePath = Path.Combine(_root, "dialogue-tone.wav");
            WriteTone(deepDialoguePath, 8);
            Check(_player.Play(deepDialoguePath, _host.Female, true, false, 1, 1), "dialogue queued after deep cue");
            Check(PublicApi.TryRequestPriorityDeepCue(deepOwner) && Cue.isPlaying && Dialogue.clip == null,
                "deep hit clears queued dialogue and replaces cue");
            _player.SetImpactCueEnabled(deepOwner, false);
            yield return WaitUntil(() => !State.Busy, 2, "deep cue stops cleanly");
            _done = true;
            File.WriteAllText(Path.Combine(_root, "runtime-result.txt"), "DEEP_CUE_RUNTIME_OK checks=" + _checks);
            Debug.Log("DEEP_CUE_RUNTIME_OK checks=" + _checks);
            _player.Dispose();
            Application.Quit(0);
            yield break;
        }
        cueSettings.CueSource.Value = "Original";

        var vrConfig = new ConfigFile(Path.Combine(_root, "smoke-vr.cfg"), false);
        var vrSettings = new MainGameVrHipIkVoiceImpactBoost.PluginSettings(vrConfig);
        vrConfig.Bind("1.全般", "MoanModeEnabled", false).Value = true;
        var vrLogger = new MainGameVrHipIkVoiceImpactBoost.PluginFileLogger();
        var vrService = new MainGameVrHipIkVoiceImpactBoost.VoiceImpactBoostService(vrSettings, vrLogger);
        var positions = new MainGameVrHipIkVoiceImpactBoost.HipIkTrackingPositionSource();
        var detector = new MainGameVrHipIkVoiceImpactBoost.HipIkMotionDetector(vrSettings, vrLogger, positions, vrService.TryRequestBoost);
        detector.Update(.01f);
        positions.Hip = Vector3.up * .05f;
        detector.Update(.01f);
        Check(Cue.isPlaying && Dialogue.clip == null, "VR detector -> service -> API -> cue with no dialogue");
        Check(_host.Female.LipSource == Cue, "standalone cue lip sync source");
        Check(!PublicApi.TryRequestImpactCue(VrOwner), "busy trigger discarded");
        var first = Cue.clip;
        yield return WaitUntil(() => !State.Busy, 6, "standalone completes");
        Check(_sequenceFinished == 0, "standalone does not finish dialogue sequence");
        Check(!PublicApi.TryRequestImpactCue(VrOwner), "150ms cooldown enforced");
        yield return new WaitForSecondsRealtime(.2f);

        // 44 real AudioSource plays cover both full shuffled bags and every reused clip.
        var clips = new HashSet<AudioClip> { first };
        AudioClip previous = first;
        for (int i = 0; i < 44; i++)
        {
            Check(PublicApi.TryRequestImpactCue(VrOwner), "cue accepted " + i);
            Check(Cue.clip != previous, "no immediate repeat " + i);
            previous = Cue.clip; clips.Add(previous);
            float[] samples = new float[previous.samples * previous.channels];
            Check(previous.GetData(samples, 0), "nonstreaming clip remains readable " + i);
            double energy = 0; foreach (float value in samples) energy += value * value;
            Check(energy > .001 && samples[0] == 0 && samples[samples.Length - 1] == 0, "repeat PCM audible and endpoints faded " + i);
            // Shorten the test through the specified OFF fade instead of waiting out every clip.
            yield return new WaitForSecondsRealtime(.12f);
            PublicApi.SetImpactCueEnabled(VrOwner, false);
            yield return WaitUntil(() => !State.Busy, 2, "OFF fade completes");
            PublicApi.SetImpactCueEnabled(VrOwner, true);
            yield return new WaitForSecondsRealtime(.2f);
        }
        Check(clips.Count == 22, "both shuffled bags contain all 22 clips");

        string dialoguePath = Path.Combine(_root, "dialogue-tone.wav");
        WriteTone(dialoguePath, 8);
        Check(_player.Play(dialoguePath, _host.Female, true, false, 1, 1), "dialogue accepted");
        yield return WaitUntil(() => Dialogue.isPlaying, 3, "dialogue starts");
        yield return new WaitForSecondsRealtime(.25f);
        int before = Dialogue.timeSamples;
        Check(PublicApi.TryRequestImpactCue(VrOwner), "cue during dialogue accepted");
        yield return WaitUntil(() => State.PausedDialogue, 2, "dialogue sample fade then pause");
        int paused = Dialogue.timeSamples;
        Check(paused >= before && paused < before + Dialogue.clip.frequency / 2, "pause position after short fade");
        // Pause is submitted to Unity's audio thread. timeSamples settles on the next DSP buffer.
        AudioSettings.GetDSPBufferSize(out int bufferFrames, out int bufferCount);
        yield return new WaitForSecondsRealtime((float)bufferFrames * bufferCount / AudioSettings.outputSampleRate + .03f);
        int settledPause = Dialogue.timeSamples;
        Check(settledPause >= paused && settledPause - paused <= (float)bufferFrames * (bufferCount + 1) * Dialogue.clip.frequency / AudioSettings.outputSampleRate,
            "native pause settles within DSP buffering");
        paused = settledPause;
        yield return new WaitForSecondsRealtime(.2f);
        Debug.Log("PAUSE_PROBE before=" + before + " paused=" + paused + " now=" + Dialogue.timeSamples
            + " playing=" + Dialogue.isPlaying + " retained=" + (Dialogue.clip != null) + " phase=" + State.Phase);
        Check(Dialogue.timeSamples == paused && Dialogue.clip != null && !Dialogue.isPlaying, "paused stream position and clip retained");
        Check(_host.Female.LipSource == Cue && _sequenceFinished == 0, "cue owns lip sync without advancing sequence");
        yield return WaitUntil(() => !State.Busy, 6, "cue ends and dialogue resumes");
        Check(Dialogue.isPlaying && Dialogue.timeSamples >= paused && Dialogue.timeSamples < paused + Dialogue.clip.frequency,
            "UnPause resumes same position, no reset or skipped cue duration");
        Check(_host.Female.LipSource == Dialogue, "dialogue lip sync restored");
        Check(!PublicApi.TryRequestImpactCue(VrOwner), "dialogue downtime rejects immediate insertion");
        yield return WaitUntil(() => Time.unscaledTime >= State.NextAllowedAt, 9, "dialogue downtime finishes");
        Check(PublicApi.TryRequestImpactCue(VrOwner), "dialogue insertion before OFF");
        yield return WaitUntil(() => State.PausedDialogue, 2, "OFF scenario paused");
        int offPosition = Dialogue.timeSamples;
        PublicApi.SetImpactCueEnabled("inactive detector", false);
        Check(Cue.isPlaying, "inactive detector cannot cancel another owner's cue");
        PublicApi.SetImpactCueEnabled(VrOwner, false);
        yield return WaitUntil(() => !State.Busy, 2, "OFF fades and resumes");
        Check(Dialogue.isPlaying && Dialogue.timeSamples >= offPosition && _host.Female.LipSource == Dialogue,
            "OFF restores position and lip sync");
        Check(PublicApi.TryRequestTransientVolumeBoost(1.3f, 20, 20, 80), "mode OFF retains legacy boost API");
        PublicApi.SetImpactCueEnabled(VrOwner, true);
        yield return new WaitForSecondsRealtime(.2f);
        Check(PublicApi.TryRequestImpactCue(VrOwner), "second dialogue insertion");
        yield return WaitUntil(() => State.PausedDialogue, 2, "second pause");
        _player.Stop("explicit stop test");
        yield return new WaitForSecondsRealtime(.3f);
        Check(!_player.IsPlaying && Dialogue.clip == null && !Cue.isPlaying && !State.PausedDialogue, "explicit stop never resumes old dialogue");

        // Incoming dialogue during standalone insertion waits until it completes.
        Check(PublicApi.TryRequestImpactCue(VrOwner), "standalone before incoming dialogue");
        Check(_player.Play(dialoguePath, _host.Female, true, false, 1, 1), "incoming dialogue accepted behind cue");
        yield return new WaitForSecondsRealtime(.2f);
        Check(Cue.isPlaying && !Dialogue.isPlaying && Dialogue.clip == null, "incoming background load waits behind cue");
        yield return WaitUntil(() => Dialogue.isPlaying, 6, "incoming dialogue starts after cue");
        _player.Stop("queue test done");

        Check(PublicApi.TryRequestImpactCue(VrOwner), "standalone before dialogue sequence");
        Check(_player.PlaySequence(new List<ExternalVoicePlaybackItem> { new ExternalVoicePlaybackItem { Path = dialoguePath } },
            "cue-sequence", _host.Female, true, false, 1, 1), "sequence accepted behind cue");
        yield return new WaitForSecondsRealtime(.2f);
        Check(Cue.isPlaying && !Dialogue.isPlaying, "sequence load does not interrupt standalone cue");
        yield return WaitUntil(() => Dialogue.isPlaying, 6, "queued sequence starts after cue");
        yield return new WaitForSecondsRealtime(.2f);
        Check(PublicApi.TryRequestImpactCue(VrOwner), "cue before paused scene exit");
        yield return WaitUntil(() => State.PausedDialogue, 2, "scene exit while paused");
        CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom = false;
        yield return null;
        yield return null;
        Check(!_player.IsPlaying && Dialogue.clip == null && !State.PausedDialogue, "scene exit releases paused dialogue and queue");
        CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom = true;

        Check(PublicApi.TryRequestImpactCue(VrOwner), "cue before scene exit");
        CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom = false;
        yield return null;
        yield return null;
        Check(!_player.IsPlaying && !Cue.isPlaying, "scene gate cancels cue and dialogue");
        CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom = true;
        yield return new WaitForSecondsRealtime(.2f);
        Check(!Dialogue.isPlaying, "scene return does not resurrect dialogue");

        var normalConfig = new MainGameVoiceImpactBoost.PluginConfig(new ConfigFile(Path.Combine(_root, "smoke-normal.cfg"), false));
        normalConfig.MoanModeEnabled.Value = true;
        const string normalOwner = "com.kks.main.voiceimpactboost";
        PublicApi.SetImpactCueEnabled(normalOwner, true);
        new MainGameVoiceImpactBoost.VoiceImpactBoostService(normalConfig, new MainGameVoiceImpactBoost.PluginFileLogger()).HandleBellyBokoPeak(1);
        Check(Cue.isPlaying && Dialogue.clip == null, "normal belly peak without dialogue");
        _player.Stop("done");
        _done = true;
        File.WriteAllText(Path.Combine(_root, "runtime-result.txt"), "UNITY_RUNTIME_OK checks=" + _checks + "\nAudioSource/Pause/UnPause verified. VR input and character are adapters.\n");
        Debug.Log("UNITY_RUNTIME_OK checks=" + _checks);
        _player.Dispose();
        Application.Quit(0);
    }

    private void Update()
    {
        if (!_done) _player?.Update();
    }
    private IEnumerator WaitUntil(Func<bool> predicate, float timeout, string label)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
        Check(predicate(), label);
    }
    private void Check(bool condition, string label)
    {
        if (!condition) { Fail(label); throw new InvalidOperationException(label); }
        _checks++; Debug.Log("PASS " + label);
    }
    private void OnLog(string text, string stack, LogType type)
    {
        if (!_done && (type == LogType.Exception || type == LogType.Error)) Fail(text);
    }
    private void Fail(string text)
    {
        if (_done) return;
        _done = true;
        if (_root != null) File.WriteAllText(Path.Combine(_root, "runtime-result.txt"), "UNITY_RUNTIME_FAILED " + text);
        Application.Quit(1);
    }
    private static void WriteTone(string path, int seconds, int amplitude = 3000)
    {
        const int rate = 22050;
        int count = seconds * rate;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            for (int i = 0; i < count; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 220 / rate) * amplitude));
        }
    }
}
