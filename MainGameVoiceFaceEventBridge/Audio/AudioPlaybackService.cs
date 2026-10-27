using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge.Audio
{
    // Unity AudioSource、WAVバックグラウンドロード、再生キュー、リップシンクの
    // 所有境界。Pluginはこのサービスだけを参照し、具体プレイヤーの寿命を扱わない。
    internal sealed class AudioPlaybackService : IDisposable
    {
        private readonly ExternalVoicePlayer _player;
        private bool _disposed;

        public AudioPlaybackService(
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            _player = new ExternalVoicePlayer(logInfo, logWarn, logError);
        }

        /// <summary>連続再生を最後まで流し終えた時。中断では発火しない。</summary>
        public event Action SequenceFinished
        {
            add { _player.SequenceFinished += value; }
            remove { _player.SequenceFinished -= value; }
        }

        public event Action<ExternalVoicePlaybackStartedEvent> PlaybackStarted
        {
            add { _player.PlaybackStarted += value; }
            remove { _player.PlaybackStarted -= value; }
        }

        public bool IsPlaying => _player.IsPlaying;
        internal bool IsThoughtPlaying => _player.IsThoughtPlaying;
        internal bool ImpactCueBusy => _player.ImpactCueBusy;
        internal bool ImpactCueActive => _player.ImpactCueActive;
        internal bool PriorityDeepCueActive => _player.PriorityDeepCueActive;
        internal bool PriorityAibuCueActive => _player.PriorityAibuCueActive;
        internal bool DeepCueReady => _player.DeepCueReady;
        internal bool CanStartPistonCue(string owner) => _player.CanStartPistonCue(owner);
        internal bool CanStartHardPistonCue(string owner) => _player.CanStartHardPistonCue(owner);
        internal bool HardPistonCuePlaying => _player.HardPistonCuePlaying;
        internal bool OgazmCuePlaying => _player.OgazmCuePlaying;
        internal bool SpecialAibuCuePlaying => _player.SpecialAibuCuePlaying;
        internal bool CanStartOgazmCue(string owner) => _player.CanStartOgazmCue(owner);
        internal void InitializeSpatialVoice(SpatialVoiceSettings settings) => _player.InitializeSpatialVoice(settings);
        internal void SetGameVoiceBusyProbe(Func<bool> probe) => _player.SetGameVoiceBusyProbe(probe);
        internal void SetDialogueVolumeProvider(Func<float> provider) => _player.SetDialogueVolumeProvider(provider);

        internal void InitializeImpactCues(string directory, ImpactCueSettings settings)
            => _player.InitializeImpactCues(directory, settings);
        internal bool RequestImpactCue(string owner, ChaControl female) => _player.RequestImpactCue(owner, female);
        internal bool RequestPriorityDeepCue(string owner, ChaControl female) => _player.RequestPriorityDeepCue(owner, female);
        internal bool RequestContinuousPistonCue(string owner, ChaControl female, float volume,
            float cooldownSeconds, out string subtitle, out float duration)
            => _player.RequestContinuousPistonCue(owner, female, volume, cooldownSeconds, out subtitle, out duration);
        internal bool RequestHardPistonCue(string owner, ChaControl female, float volume,
            float cooldownSeconds, out string subtitle, out float duration)
            => _player.RequestHardPistonCue(owner, female, volume, cooldownSeconds, out subtitle, out duration);
        internal bool RequestOgazmCue(string owner, ChaControl female, float volume,
            float cooldownSeconds, out string subtitle, out float duration)
            => _player.RequestOgazmCue(owner, female, volume, cooldownSeconds, out subtitle, out duration);
        internal void SetImpactCueEnabled(string owner, bool enabled) => _player.SetImpactCueEnabled(owner, enabled);
        internal void CancelImpactCueForMotion() => _player.CancelImpactCueForMotion();

        public bool Play(
            string absolutePath,
            ChaControl female,
            bool interruptCurrent,
            bool deleteAfterPlay,
            float volume,
            float pitch)
        {
            return _player.Play(
                absolutePath,
                female,
                interruptCurrent,
                deleteAfterPlay,
                volume,
                pitch);
        }

        public bool PlaySequence(
            IList<ExternalVoicePlaybackItem> items,
            string sessionId,
            ChaControl female,
            bool interruptCurrent,
            bool defaultDeleteAfterPlay,
            float volume,
            float pitch)
        {
            return _player.PlaySequence(
                items,
                sessionId,
                female,
                interruptCurrent,
                defaultDeleteAfterPlay,
                volume,
                pitch);
        }

        public bool AppendSequence(
            IList<ExternalVoicePlaybackItem> items,
            string sessionId,
            ChaControl female,
            bool defaultDeleteAfterPlay,
            float volume,
            float pitch)
        {
            return _player.AppendSequence(
                items,
                sessionId,
                female,
                defaultDeleteAfterPlay,
                volume,
                pitch);
        }

        public void Stop(string reason)
        {
            _player.Stop(reason);
        }

        public void Tick()
        {
            _player.Update();
        }

        public bool RequestTransientBoost(
            float peakMultiplier,
            float attackSec,
            float holdSec,
            float releaseSec,
            float silenceSec,
            float silenceFadeOutSec,
            float silenceFadeInSec,
            PublicApi.EasingShape easing)
        {
            return _player.RequestTransientBoost(
                peakMultiplier,
                attackSec,
                holdSec,
                releaseSec,
                silenceSec,
                silenceFadeOutSec,
                silenceFadeInSec,
                easing);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _player.Dispose();
        }
    }
}
