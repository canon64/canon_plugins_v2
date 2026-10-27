using System;
using System.Collections.Generic;
using System.Text;
using MainGameVoiceFaceEventBridge.Game;
using CanonPlugins.Shared;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private readonly GameVoiceLoadLedger<Manager.Voice.Loader> _nativeVoiceLoads = new GameVoiceLoadLedger<Manager.Voice.Loader>();
        private bool _nativeVoiceProbeFailureLogged;
        private float _nextNativeVoiceDetailAt;
        private sealed class NativeVoiceStandby
        {
            internal AudioSource Source;
            internal Manager.Voice.Loader Loader;
        }
        private readonly List<NativeVoiceStandby> _nativeVoiceStandbys = new List<NativeVoiceStandby>();

        internal GameVoiceLoadLedger<Manager.Voice.Loader>.Lease BeginNativeVoiceLoad(Manager.Voice.Loader loader)
            => IsCurrentSceneVoice(loader) ? _nativeVoiceLoads.Begin(loader) : null;

        internal void FinishNativeVoiceLoad(GameVoiceLoadLedger<Manager.Voice.Loader>.Lease lease)
            => _nativeVoiceLoads.Finish(lease);

        internal bool RejectNativeVoiceStandby(Manager.Voice.Loader loader)
            => _nativeVoiceLoads.IsCancelled(loader) || ShouldBlockGameVoiceEvents();

        internal void CancelNativeVoiceLoads(int? no, Transform target, bool hasTarget)
        {
            Predicate<Manager.Voice.Loader> matches = loader => (!no.HasValue || loader.no == no.Value)
                && (!hasTarget || loader.voiceTrans == target);
            _nativeVoiceLoads.CancelWhere(matches);
            _nativeVoiceStandbys.RemoveAll(standby => matches(standby.Loader));
        }

        internal void RegisterNativeVoiceStandby(AudioSource source, Manager.Voice.Loader loader)
        {
            if (source != null && IsCurrentSceneVoice(loader))
                _nativeVoiceStandbys.Add(new NativeVoiceStandby { Source = source, Loader = loader });
        }

        private void TickNativeVoiceStandbys()
        {
            for (int i = _nativeVoiceStandbys.Count - 1; i >= 0; --i)
            {
                NativeVoiceStandby standby = _nativeVoiceStandbys[i];
                AudioSource source = standby.Source;
                if (source == null || !IsCurrentSceneVoice(standby.Loader)
                    || !source.isActiveAndEnabled || source.clip == null
                    || source.clip.loadState == AudioDataLoadState.Failed)
                {
                    // Prevent an old deferred Play subscription from firing after release.
                    if (source != null) UnityEngine.Object.Destroy(source.gameObject);
                    _nativeVoiceStandbys.RemoveAt(i);
                }
                else if (source.isPlaying)
                {
                    // Once started, its real playback state owns admission, not object lifetime.
                    _nativeVoiceStandbys.RemoveAt(i);
                }
            }
        }

        private void ResetNativeVoiceAdmission()
        {
            _nativeVoiceLoads.Reset();
            foreach (NativeVoiceStandby standby in _nativeVoiceStandbys)
                if (standby.Source != null) UnityEngine.Object.Destroy(standby.Source.gameObject);
            _nativeVoiceStandbys.Clear();
        }

        private bool IsCurrentSceneVoice(Manager.Voice.Loader loader)
        {
            if (loader == null || !BlankRoomGate.IsInBlankRoom) return false;
            HSceneProc proc = FindCurrentProc();
            if (proc == null || proc.flags == null) return false;
            if (!ReferenceEquals(loader.voiceTrans, null))
            {
                if (loader.voiceTrans == null) return false; // Destroyed target, not a 2D request.
                var mouths = proc.flags.transVoiceMouth;
                if (mouths != null)
                    foreach (Transform mouth in mouths)
                        if (mouth != null && mouth == loader.voiceTrans) return true;
                return false;
            }
            var heroines = proc.flags.lstHeroine;
            if (heroines != null)
                foreach (var heroine in heroines)
                    if (heroine != null && heroine.voiceNo == loader.no) return true;
            return false;
        }

        private bool IsGameVoicePlayingOrPending()
        {
            if (!BlankRoomGate.IsInBlankRoom) return false;
            try
            {
                _nativeVoiceLoads.CancelWhere(loader => !IsCurrentSceneVoice(loader));
                TickNativeVoiceStandbys();
                if (_nativeVoiceLoads.HasPending || _nativeVoiceStandbys.Count > 0)
                {
                    if (ImpactCueDiagnosticsEnabled && Time.unscaledTime >= _nextNativeVoiceDetailAt)
                    {
                        _nextNativeVoiceDetailAt = Time.unscaledTime + 1f;
                        var detail = new StringBuilder("native busy: pendingLoads=");
                        int pendingCount = 0;
                        foreach (Manager.Voice.Loader loader in _nativeVoiceLoads.PendingOwners)
                        {
                            if (pendingCount++ < 4) detail.Append(" [no=").Append(loader?.no).Append(" target=").Append(loader?.voiceTrans?.name ?? "<none>").Append(']');
                        }
                        detail.Append(" count=").Append(pendingCount).Append(" standbys=").Append(_nativeVoiceStandbys.Count);
                        for (int i = 0; i < _nativeVoiceStandbys.Count && i < 4; i++)
                        {
                            AudioSource source = _nativeVoiceStandbys[i].Source;
                            detail.Append(" [no=").Append(_nativeVoiceStandbys[i].Loader?.no).Append(" clip=").Append(source?.clip?.name ?? "<none>")
                                .Append(" loadState=").Append(source?.clip?.loadState.ToString() ?? "<none>")
                                .Append(" playing=").Append(source != null && source.isPlaying).Append(']');
                        }
                        LogImpactCueDiagnostic(detail.ToString());
                    }
                    return true;
                }
                HSceneProc proc = FindCurrentProc();
                if (proc == null || proc.flags == null)
                {
                    LogImpactCueDiagnostic("native busy: HSceneProc or flags missing");
                    return true;
                }
                var heroines = proc.flags.lstHeroine;
                bool playing = false;
                StringBuilder playingDetail = null;
                if (heroines != null)
                    foreach (var heroine in heroines)
                    {
                        if (heroine == null) continue;
                        // Manager.Voice.IsPlay counts child objects, including silent/stopped ones.
                        foreach (AudioSource source in Manager.Voice.ToPlaying(heroine.voiceNo))
                        {
                            if (source == null || !source.isPlaying) continue;
                            playing = true;
                            if (!ImpactCueDiagnosticsEnabled) return true;
                            if (Time.unscaledTime < _nextNativeVoiceDetailAt) continue;
                            if (playingDetail == null) playingDetail = new StringBuilder("native busy: playing sources");
                            playingDetail.Append(" [voiceNo=").Append(heroine.voiceNo)
                                .Append(" object=").Append(source.gameObject.name)
                                .Append(" clip=").Append(source.clip != null ? source.clip.name : "<none>")
                                .Append(" time=").Append(source.time.ToString("F2"))
                                .Append(" loop=").Append(source.loop)
                                .Append(" volume=").Append(source.volume.ToString("F2"))
                                .Append(']');
                        }
                    }
                if (playing)
                {
                    if (playingDetail != null)
                    {
                        _nextNativeVoiceDetailAt = Time.unscaledTime + 1f;
                        LogImpactCueDiagnostic(playingDetail.ToString());
                    }
                    return true;
                }
                _nativeVoiceProbeFailureLogged = false;
                return false;
            }
            catch (Exception ex)
            {
                LogImpactCueDiagnostic("native busy: probe exception " + ex);
                if (!_nativeVoiceProbeFailureLogged) LogWarn("[impact-cue] game voice probe failed; skip cue: " + ex.Message);
                _nativeVoiceProbeFailureLogged = true;
                return true;
            }
        }
    }
}
