using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private float _nextAudioDiagnosticsLogTime = 0f;

        private void TryLogAudioDiagnosticsTick()
        {
            if (_settings == null || !_settings.EnableAudioDiagnosticsLog)
                return;

            if (_mainVideoPlayer == null)
                return;

            bool isPlaying;
            try
            {
                isPlaying = _mainVideoPlayer.isPlaying;
            }
            catch
            {
                return;
            }

            if (!isPlaying)
                return;

            if (Time.unscaledTime < _nextAudioDiagnosticsLogTime)
                return;

            _nextAudioDiagnosticsLogTime = Time.unscaledTime + 3f;
            LogAudioDiagnosticsSnapshot("tick", includeStoppedSources: true);
        }

        private void LogAudioDiagnosticsSnapshot(string context, bool includeStoppedSources)
        {
            if (_settings == null || !_settings.EnableAudioDiagnosticsLog)
                return;

            try
            {
                LogInfo(
                    $"[audio-snap] begin context={context} " +
                    $"cfgMute={(_settings?.MuteVideoAudio ?? false)} " +
                    $"cfgVideoVolume={(_settings?.VideoVolume ?? -1f):F3}");

                var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                if (listener != null)
                {
                    LogInfo(
                        $"[audio-snap] listener pos={listener.transform.position} " +
                        $"rot={listener.transform.eulerAngles} name={listener.name}");
                }
                else
                {
                    LogWarn("[audio-snap] listener not found");
                }

                var players = CollectKnownVideoPlayers();
                LogInfo($"[audio-snap] videoPlayers count={players.Count}");
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null) continue;

                    bool isPrepared = false;
                    bool isPlaying = false;
                    bool directMute = false;
                    float directVolume = float.NaN;
                    double time = double.NaN;
                    long frame = -1L;
                    string url = string.Empty;
                    VideoAudioOutputMode mode = VideoAudioOutputMode.None;
                    AudioSource target = null;

                    try { isPrepared = p.isPrepared; } catch { }
                    try { isPlaying = p.isPlaying; } catch { }
                    try { time = p.time; } catch { }
                    try { frame = p.frame; } catch { }
                    try { url = p.url; } catch { }
                    try { mode = p.audioOutputMode; } catch { }
                    try { directMute = p.GetDirectAudioMute((ushort)0); } catch { }
                    try { directVolume = p.GetDirectAudioVolume((ushort)0); } catch { }
                    try { target = p.GetTargetAudioSource((ushort)0); } catch { }

                    LogInfo(
                        $"[audio-snap] vp[{i}] id={p.GetInstanceID()} " +
                        $"main={ReferenceEquals(p, _mainVideoPlayer)} " +
                        $"mode={mode} prepared={isPrepared} playing={isPlaying} " +
                        $"time={time:F3} frame={frame} directMute={directMute} " +
                        $"directVol={directVolume:F3} target={FormatAudioSourceName(target)} url={url}");
                }

                var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
                Array.Sort(
                    sources,
                    (a, b) => string.CompareOrdinal(
                        BuildTransformPath(a != null ? a.transform : null),
                        BuildTransformPath(b != null ? b.transform : null)));

                int playingCount = 0;
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] != null && sources[i].isPlaying)
                        playingCount++;
                }

                LogInfo(
                    $"[audio-snap] sources total={sources.Length} " +
                    $"playing={playingCount} includeStopped={includeStoppedSources}");

                int index = 0;
                for (int i = 0; i < sources.Length; i++)
                {
                    var s = sources[i];
                    if (s == null) continue;
                    if (!includeStoppedSources && !s.isPlaying) continue;

                    float distanceToListener = float.NaN;
                    if (listener != null)
                    {
                        distanceToListener = Vector3.Distance(
                            listener.transform.position,
                            s.transform.position);
                    }

                    string clipName = s.clip != null ? s.clip.name : "(null)";
                    LogInfo(
                        $"[audio-snap] src[{index}] id={s.GetInstanceID()} " +
                        $"name={s.name} path={BuildTransformPath(s.transform)} " +
                        $"playing={s.isPlaying} enabled={s.enabled} mute={s.mute} " +
                        $"vol={s.volume:F3} pitch={s.pitch:F3} pan={s.panStereo:F3} " +
                        $"spatial={s.spatialBlend:F3} loop={s.loop} priority={s.priority} " +
                        $"bypassRZ={s.bypassReverbZones} rzMix={s.reverbZoneMix:F3} " +
                        $"min={s.minDistance:F2} max={s.maxDistance:F2} " +
                        $"dist={distanceToListener:F2} clip={clipName} time={s.time:F3}");
                    index++;
                }

                LogInfo($"[audio-snap] end context={context}");
            }
            catch (Exception ex)
            {
                LogWarn($"[audio-snap] failed context={context} error={ex.Message}");
            }
        }

        private List<VideoPlayer> CollectKnownVideoPlayers()
        {
            var list = new List<VideoPlayer>();
            var seen = new HashSet<int>();

            if (_videoBindings != null)
            {
                foreach (var kv in _videoBindings)
                {
                    var p = kv.Value?.Player;
                    if (p == null) continue;
                    int id = p.GetInstanceID();
                    if (!seen.Add(id)) continue;
                    list.Add(p);
                }
            }

            if (_mainVideoPlayer != null)
            {
                int id = _mainVideoPlayer.GetInstanceID();
                if (seen.Add(id))
                    list.Add(_mainVideoPlayer);
            }

            return list;
        }

        private static string BuildTransformPath(Transform transform)
        {
            if (transform == null) return "(null)";

            var stack = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack.ToArray());
        }

        private static string FormatAudioSourceName(AudioSource source)
        {
            if (source == null) return "(null)";
            return $"{source.name}#{source.GetInstanceID()}";
        }
    }
}
