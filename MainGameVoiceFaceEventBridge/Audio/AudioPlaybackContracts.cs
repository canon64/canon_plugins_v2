namespace MainGameVoiceFaceEventBridge
{
    internal sealed class ExternalVoicePlaybackItem
    {
        internal int Index;
        internal int Total;
        internal int SequencePosition;
        internal string Path;
        internal string Subtitle;
        internal string FullSubtitle;
        internal float FullHoldSeconds;
        internal float DurationSeconds;
        internal float HoldSeconds;
        internal bool DeleteAfterPlay;
        internal bool IsThought;
    }

    internal sealed class ExternalVoicePlaybackStartedEvent
    {
        internal string SessionId;
        internal int Index;
        internal int Total;
        internal int SequencePosition;
        internal string Path;
        internal string Subtitle;
        internal string FullSubtitle;
        internal float FullHoldSeconds;
        internal float DurationSeconds;
        internal float HoldSeconds;
    }
}
