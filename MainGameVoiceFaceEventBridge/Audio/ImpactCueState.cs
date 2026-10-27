using System;

namespace MainGameVoiceFaceEventBridge.Audio
{
    [Flags]
    internal enum CueAction
    {
        None = 0, FadeDialogue = 1, PauseDialogue = 2, StartCue = 4,
        FadeCue = 8, FinishCue = 16, ResumeDialogue = 32
    }

    // Main-thread state only. A rejected trigger is never queued.
    internal sealed class ImpactCueState
    {
        internal enum Stage { Idle, FadingDialogue, PlayingCue, StoppingCue, ResumingDialogue }
        internal Stage Phase { get; private set; }
        internal bool PausedDialogue { get; private set; }
        internal bool Busy => Phase != Stage.Idle;
        internal bool HoldsDialogue => Busy && Phase != Stage.ResumingDialogue;
        internal double NextAllowedAt { get; private set; }
        private bool _protectDialogue;
        private double _cueStartedAt = double.NaN;
        private double _cueDurationSeconds;

        internal bool TryBegin(double now, bool dialoguePlaying, out CueAction action)
        {
            action = CueAction.None;
            if (Busy || now < NextAllowedAt) return false;
            _protectDialogue = dialoguePlaying;
            _cueStartedAt = double.NaN;
            _cueDurationSeconds = 0;
            PausedDialogue = false;
            Phase = dialoguePlaying ? Stage.FadingDialogue : Stage.PlayingCue;
            action = dialoguePlaying ? CueAction.FadeDialogue : CueAction.StartCue;
            return true;
        }

        internal void RecordCueStarted(double now, double durationSeconds)
        {
            _cueStartedAt = now;
            _cueDurationSeconds = Math.Max(0, durationSeconds);
        }

        internal CueAction Tick(double now, bool dialoguePlaying, bool dialogueFadeDone,
            bool cuePlaying, bool cueFadeDone, int cooldownMs, float dialogueDowntimeMultiplier = 2f)
        {
            if (Phase == Stage.FadingDialogue && (dialogueFadeDone || !dialoguePlaying))
            {
                PausedDialogue = dialoguePlaying;
                Phase = Stage.PlayingCue;
                return CueAction.StartCue | (PausedDialogue ? CueAction.PauseDialogue : CueAction.None);
            }
            if ((Phase == Stage.PlayingCue && !cuePlaying)
                || (Phase == Stage.StoppingCue && (!cuePlaying || cueFadeDone)))
            {
                // Measure the cue itself, excluding dialogue fades. An early OFF counts only the played portion.
                double playedSeconds = double.IsNaN(_cueStartedAt) ? 0
                    : Math.Min(_cueDurationSeconds, Math.Max(0, now - _cueStartedAt));
                double dialogueDowntime = _protectDialogue ? playedSeconds * Math.Max(0, dialogueDowntimeMultiplier) : 0;
                NextAllowedAt = now + Math.Max(Math.Max(0, cooldownMs) * 0.001, dialogueDowntime);
                bool resume = PausedDialogue;
                PausedDialogue = false;
                Phase = resume ? Stage.ResumingDialogue : Stage.Idle;
                return CueAction.FinishCue | (resume ? CueAction.ResumeDialogue : CueAction.None);
            }
            if (Phase == Stage.ResumingDialogue && (dialogueFadeDone || !dialoguePlaying))
                Phase = Stage.Idle;
            return CueAction.None;
        }

        internal CueAction Disable()
        {
            if (Phase == Stage.FadingDialogue)
            {
                Phase = Stage.ResumingDialogue;
                return CueAction.FinishCue | CueAction.ResumeDialogue;
            }
            if (Phase == Stage.PlayingCue)
            {
                Phase = Stage.StoppingCue;
                return CueAction.FadeCue;
            }
            return CueAction.None;
        }

        // Replacement dialogue may supersede a paused line, but does not cut off the cue.
        internal CueAction ForgetDialogue()
        {
            PausedDialogue = false;
            if (Phase == Stage.FadingDialogue)
            {
                Phase = Stage.PlayingCue;
                return CueAction.StartCue;
            }
            if (Phase == Stage.ResumingDialogue) Phase = Stage.Idle;
            return CueAction.None;
        }

        internal void Abort()
        {
            Phase = Stage.Idle;
            PausedDialogue = false;
            NextAllowedAt = 0;
            _protectDialogue = false;
            _cueStartedAt = double.NaN;
            _cueDurationSeconds = 0;
        }
    }

    internal sealed class CueShuffleBag
    {
        private readonly Random _random;
        private readonly int[] _bag;
        private int _position;
        private int _last = -1;
        internal CueShuffleBag(int count, Random random)
        {
            _bag = new int[count];
            _random = random;
            _position = count;
        }
        internal int Next()
        {
            if (_bag.Length == 0) return -1;
            if (_position == _bag.Length)
            {
                for (int i = 0; i < _bag.Length; i++) _bag[i] = i;
                for (int i = _bag.Length - 1; i > 0; i--)
                {
                    int j = _random.Next(i + 1);
                    int value = _bag[i]; _bag[i] = _bag[j]; _bag[j] = value;
                }
                if (_bag.Length > 1 && _bag[0] == _last)
                {
                    int j = _random.Next(1, _bag.Length);
                    int value = _bag[0]; _bag[0] = _bag[j]; _bag[j] = value;
                }
                _position = 0;
            }
            return _last = _bag[_position++];
        }
    }
}
