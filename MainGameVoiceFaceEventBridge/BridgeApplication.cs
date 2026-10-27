using System;

namespace MainGameVoiceFaceEventBridge
{
    // 構成ルート。責務サービスを生成し、その寿命だけを所有する。
    // ゲームオブジェクトや設定値を集約する万能Contextにはしない。
    internal sealed class BridgeApplication : IDisposable
    {
        private bool _disposed;

        public BridgeApplication(
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            InboundQueue = new Inbound.InboundCommandQueue();
            InboundDecoder = new Inbound.InboundCommandDecoder();
            ActionScheduler = new Scheduling.StableActionScheduler();
            SequenceSessions = new Scheduling.SequenceSessionCoordinator(ActionScheduler);
            ResponseTextQueue = new ResponseText.ResponseTextCommandQueue();
            VoiceGuardState = new Game.GameVoiceGuardState();
            SubtitleEvents = new External.SubtitleEventClient(logInfo, logWarn);
            Audio = new Audio.AudioPlaybackService(logInfo, logWarn, logError);
        }

        public Inbound.InboundCommandQueue InboundQueue { get; }
        public Inbound.InboundCommandDecoder InboundDecoder { get; }
        public Scheduling.StableActionScheduler ActionScheduler { get; }
        public Scheduling.SequenceSessionCoordinator SequenceSessions { get; }
        public ResponseText.ResponseTextCommandQueue ResponseTextQueue { get; }
        public Game.GameVoiceGuardState VoiceGuardState { get; }
        public External.SubtitleEventClient SubtitleEvents { get; }
        public Audio.AudioPlaybackService Audio { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SequenceSessions.Deactivate();
            SequenceSessions.ClearTransientState();
            Audio.Dispose();
            VoiceGuardState.Reset();
        }
    }
}
