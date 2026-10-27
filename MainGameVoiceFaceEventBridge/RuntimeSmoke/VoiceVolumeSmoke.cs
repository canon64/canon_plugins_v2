using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MainGameVoiceFaceEventBridge;
using MainGameVoiceFaceEventBridge.Audio;
using UnityEngine;

public sealed partial class RuntimeSmoke
{
    private float EffectiveDialogueVolume => Dialogue.volume * PluginSettings.MaxFemalePlaybackVolume;
    private IEnumerator CheckVoiceVolume()
    {
        const string json = "{\"type\":\"speak_sequence\",\"items\":[{\"audioPath\":\"example.wav\"}]}";
        Check(CommandParser.TryParseIncoming(json, new PluginSettings(), out var command, out var reason), reason);
        Debug.Log("VOICE_VOLUME_MISSING_FIELD parsed=" + command.volume);
        Check(command.volume == -1f, "omitted JSON volume keeps the default-volume sentinel");

        var settings = new PluginSettings { PlaybackVolume = 0f, FemalePlaybackVolume = 1f };
        _player.SetDialogueVolumeProvider(settings.ResolveFemalePlaybackVolume);
        string path = Path.Combine(_root, "voice-volume-tone.wav");
        WriteTone(path, 2);
        Check(_player.Play(path, null, true, false, command.ResolveVolume(1f), 1), "default-volume WAV accepted");
        yield return WaitUntil(() => Dialogue.isPlaying, 3, "default-volume WAV starts");
        var probe = Dialogue.gameObject.AddComponent<VoiceVolumeProbe>();
        AudioListener.volume = 1f; // The final probe silences output after measuring it; keep DSP active.
        var filter = Field<VolumeBoostFilter>("_boostFilter");
        Check(Mathf.Approximately(EffectiveDialogueVolume, 1f), "female 100 percent remains audible with fallback volume zero");
        yield return new WaitForSecondsRealtime(.15f);
        float normalPeak = probe.Peak;
        Debug.Log("VOICE_VOLUME_PROBE blocks=" + probe.BlockCount + " peak=" + normalPeak);
        Check(probe.BlockCount > 0 && normalPeak > .01f, "audio thread produces a measurable 100 percent PCM signal");
        int before = Dialogue.timeSamples;
        settings.FemalePlaybackVolume = 0f;
        _player.Update();
        Check(Dialogue.volume == 0f && Dialogue.isPlaying, "live mute retains playback");
        yield return new WaitForSecondsRealtime(.1f);
        settings.FemalePlaybackVolume = .4f;
        _player.Update();
        Check(Mathf.Approximately(EffectiveDialogueVolume, .4f) && Dialogue.timeSamples > before,
            "live unmute restores volume without restarting speech");

        settings.FemalePlaybackVolume = -1f;
        settings.PlaybackVolume = .7f;
        _player.Update();
        Check(Mathf.Approximately(EffectiveDialogueVolume, .7f), "negative female setting uses live fallback volume");

        settings.FemalePlaybackVolume = 5f;
        settings.Normalize();
        Check(settings.ResolveFemalePlaybackVolume() == 5f, "normalization preserves 500 percent");
        _player.Update();
        Check(Dialogue.volume == 1f && filter.Gain == 1f, "500 percent uses upstream PCM amplification and native volume");
        yield return new WaitForSecondsRealtime(.15f);
        Debug.Log("VOICE_VOLUME_PCM normal=" + normalPeak + " amplified=" + probe.Peak);
        Check(Mathf.Abs(probe.Peak / normalPeak - 5f) < .05f, "real audio-thread PCM reaches five times the 100 percent amplitude");
        Check(_player.RequestTransientBoost(4f, 0f, .25f, .05f, 0f, 0f, 0f, PublicApi.EasingShape.Linear),
            "transient boost accepted on top of 500 percent");
        yield return new WaitForSecondsRealtime(.15f);
        Check(probe.Peak >= .99f && probe.Peak <= 1f, "combined transient and base gain clips within the valid PCM range");
        yield return new WaitForSecondsRealtime(.3f);
        Check(filter.Gain == 1f && EffectiveDialogueVolume == 5f && Mathf.Abs(probe.Peak / normalPeak - 5f) < .05f,
            "transient completion retains 500 percent base volume");
        settings.FemalePlaybackVolume = 8f;
        settings.Normalize();
        Check(settings.FemalePlaybackVolume == 5f, "values above 500 percent are capped");
        settings.FemalePlaybackVolume = .4f;
        _player.Update();
        Check(Mathf.Approximately(EffectiveDialogueVolume, .4f) && filter.Gain == 1f,
            "lowering below 100 percent clears persistent amplification");

        Check(CommandParser.TryParseIncoming("{\"type\":\"speak\",\"volume\":0.5}", settings, out command, out reason), reason);
        Check(_player.Play(path, null, true, false, command.ResolveVolume(1f), 1), "explicit command volume accepted");
        settings.FemalePlaybackVolume = .6f; // Change while the asynchronous load is pending.
        yield return WaitUntil(() => Dialogue.isPlaying, 3, "explicit command volume starts");
        Check(Mathf.Approximately(EffectiveDialogueVolume, .3f), "pending load uses latest setting times command volume");
        settings.FemalePlaybackVolume = 5f;
        _player.Update();
        Check(Mathf.Approximately(EffectiveDialogueVolume, 2.5f), "500 percent times a half-volume command produces 250 percent");

        var items = new List<ExternalVoicePlaybackItem> { new ExternalVoicePlaybackItem { Path = path } };
        Check(_player.PlaySequence(items, "volume-smoke", null, true, false, 1f, 1f), "sequence accepted");
        yield return WaitUntil(() => Dialogue.isPlaying, 3, "sequence starts");
        AudioClip firstClip = Dialogue.clip;
        Check(_player.AppendSequence(items, "volume-smoke", null, false, .5f, 1f), "streaming append accepted");
        settings.FemalePlaybackVolume = .8f;
        _player.Update();
        Check(Mathf.Approximately(EffectiveDialogueVolume, .8f), "append does not overwrite current command volume");
        settings.FemalePlaybackVolume = 5f;
        yield return WaitUntil(() => Dialogue.isPlaying && Dialogue.clip != firstClip, 4, "appended clip starts");
        Check(Mathf.Approximately(EffectiveDialogueVolume, 2.5f), "appended clip uses 500 percent setting and its half-volume command");

        Check(CommandParser.TryParseIncoming("{\"type\":\"speak\",\"volume\":0}", settings, out command, out reason), reason);
        Check(_player.Play(path, null, true, false, command.ResolveVolume(1f), 1), "explicit mute command accepted");
        yield return WaitUntil(() => Dialogue.isPlaying, 3, "explicit mute command starts");
        Check(Dialogue.volume == 0f, "explicit command mute is preserved at 500 percent");
        settings.FemalePlaybackVolume = 1f;
        string strongPath = Path.Combine(_root, "voice-volume-strong-tone.wav");
        WriteTone(strongPath, 2, 24000);
        Check(_player.Play(strongPath, null, true, false, 1f, 1f), "high-amplitude original PCM accepted");
        yield return WaitUntil(() => Dialogue.isPlaying, 3, "high-amplitude original PCM starts");
        yield return new WaitForSecondsRealtime(.15f);
        Check(Mathf.Abs(probe.Peak / normalPeak - 8f) < .08f,
            "100 percent preserves high-amplitude input without premature clipping of the preamplified PCM");
        _done = true;
        File.WriteAllText(Path.Combine(_root, "voice-volume-result.txt"), "VOICE_VOLUME_OK checks=" + _checks);
        _player.Dispose();
        Application.Quit(0);
        yield break;
    }
}

// Added after the production filter: measures DSP output, then silences this test process.
public sealed class VoiceVolumeProbe : MonoBehaviour
{
    public volatile float Peak;
    public volatile int BlockCount;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float peak = 0f;
        for (int i = 0; i < data.Length; i++) peak = Math.Max(peak, Math.Abs(data[i]));
        Peak = peak;
        BlockCount++;
        Array.Clear(data, 0, data.Length);
    }
}
