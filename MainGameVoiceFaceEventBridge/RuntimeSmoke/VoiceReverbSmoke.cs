using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using MainGameVoiceFaceEventBridge;
using MainGameVoiceFaceEventBridge.Audio;
using UnityEngine;

public sealed partial class RuntimeSmoke
{
    private IEnumerator CheckVoiceReverb()
    {
        string mutedMarker = Path.Combine(_root, "voice-reverb-muted-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".ready");
        yield return WaitUntil(() => File.Exists(mutedMarker), 10, "isolated process output muted at Windows session");
        var capture = gameObject.AddComponent<ReverbOutputProbe>();
        AudioListener.volume = 1f;
        var settings = new PluginSettings();
        _player.SetDialogueVolumeProvider(settings.ResolveFemalePlaybackVolume);
        var spatial = new SpatialVoiceSettings(new ConfigFile(Path.Combine(_root, "reverb-smoke.cfg"), false));
        spatial.MinDistance.Value = 10;
        _player.InitializeSpatialVoice(spatial);
        var female = new ChaControl { objBodyBone = new GameObject("reverb test body") };
        var mouth = new GameObject("cf_J_MouthCavity");
        mouth.transform.SetParent(female.objBodyBone.transform);
        mouth.transform.position = Vector3.forward;
        string path = Path.Combine(_root, "reverb-burst.wav");
        WriteReverbBurst(path);
        double[] dry = new double[4], wet = new double[4];
        for (int i = 0; i < 4; i++)
        {
            spatial.Enabled.Value = i < 2;
            settings.FemalePlaybackVolume = (i % 2 == 0) ? 1f : 5f;
            var zoneObject = new GameObject("reverb test zone");
            var zone = zoneObject.AddComponent<AudioReverbZone>();
            zone.reverbPreset = AudioReverbPreset.Cave;
            zone.minDistance = 9999;
            zone.maxDistance = 10000;
            yield return new WaitForSecondsRealtime(.2f);
            Check(_player.Play(path, female, true, false, 1, 1), "reverb comparison clip accepted " + i);
            yield return WaitUntil(() => Dialogue.isPlaying, 3, "reverb comparison clip starts " + i);
            capture.Begin();
            Check(Dialogue.spatialBlend == (i < 2 ? 1f : 0f), "requested spatial mode applied " + i);
            yield return new WaitForSecondsRealtime(2f);
            capture.Recording = false;
            capture.Measure(out dry[i], out wet[i]);
            Debug.Log("REVERB_TAKE mode=" + (i < 2 ? "3D" : "2D") + " volume=" + settings.FemalePlaybackVolume
                + " dry=" + dry[i] + " tail=" + wet[i]);
            _player.Stop("reverb comparison done");
            UnityEngine.Object.Destroy(zoneObject);
            yield return new WaitForSecondsRealtime(3f); // Let the previous global reverb tail decay.
        }
        for (int i = 0; i < 4; i += 2)
        {
            double dryRatio = dry[i + 1] / dry[i], wetRatio = wet[i + 1] / wet[i];
            Debug.Log("REVERB_RATIOS mode=" + (i == 0 ? "3D" : "2D") + " dry=" + dryRatio + " tail=" + wetRatio);
            Check(dry[i] > .001 && wet[i] > .00001, "voice and reverb tail are measurable " + i);
            Check(dryRatio > 4.5 && dryRatio < 5.5, "500 percent amplifies the direct voice fivefold " + i);
            Check(wetRatio > 4.5 && wetRatio < 5.5, "500 percent amplifies the reverb tail fivefold " + i);
        }
        File.WriteAllText(Path.Combine(_root, "voice-reverb-result.txt"), "VOICE_REVERB_OK checks=" + _checks);
        _done = true;
        _player.Dispose();
        Application.Quit(0);
    }

    private static void WriteReverbBurst(string path)
    {
        const int rate = 24000, count = rate * 3;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            for (int i = 0; i < count; i++)
                writer.Write((short)(i >= rate / 10 && i < rate * 3 / 10
                    ? Math.Sin(i * 2 * Math.PI * 440 / rate) * 2000 : 0));
        }
    }
}

public sealed class ReverbOutputProbe : MonoBehaviour
{
    public bool Recording;
    private readonly float[] _buffer = new float[8192];
    private float[] _data;
    private double _lastDspTime;
    private int _count;
    public void Begin()
    {
        _lastDspTime = AudioSettings.dspTime;
        _data = new float[AudioSettings.outputSampleRate * 3];
        _count = 0;
        Recording = true;
    }
    private void LateUpdate()
    {
        if (!Recording) return;
        double now = AudioSettings.dspTime;
        int count = (int)Math.Round((now - _lastDspTime) * AudioSettings.outputSampleRate);
        if (count <= 0) return;
        _lastDspTime = now;
        if (count > _buffer.Length) throw new InvalidOperationException("audio capture polling gap");
        count = Math.Min(count, _data.Length - _count);
        AudioListener.GetOutputData(_buffer, 0);
        Array.Copy(_buffer, _buffer.Length - count, _data, _count, count);
        _count += count;
    }
    public void Measure(out double dry, out double tail)
    {
        int onset = 0;
        while (onset < _count && Math.Abs(_data[onset]) < .005f) onset++;
        int rate = AudioSettings.outputSampleRate;
        dry = Rms(onset, onset + rate / 10);
        tail = Rms(onset + rate / 2, onset + rate * 3 / 2);
    }
    private double Rms(int start, int end)
    {
        end = Math.Min(end, _count);
        double sum = 0;
        for (int i = start; i < end; i++) sum += _data[i] * _data[i];
        return Math.Sqrt(sum / Math.Max(1, end - start));
    }
}
