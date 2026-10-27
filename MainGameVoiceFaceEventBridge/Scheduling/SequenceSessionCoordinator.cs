using System;
using System.Collections.Generic;
using System.Globalization;

namespace MainGameVoiceFaceEventBridge.Scheduling
{
    internal sealed class SequenceSessionCoordinator
    {
        private readonly StableActionScheduler _scheduler;
        private readonly HashSet<string> _triggerRegisteredSessions =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _lineStartTimes =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<PendingLineAction> _pendingLineActions =
            new List<PendingLineAction>();

        public SequenceSessionCoordinator(StableActionScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public string ActiveSessionId { get; private set; } = string.Empty;
        public int RegisteredTriggerSessionCount => _triggerRegisteredSessions.Count;
        public int LineStartCount => _lineStartTimes.Count;
        public int PendingLineActionCount => _pendingLineActions.Count;

        public void Activate(string sessionId)
        {
            ActiveSessionId = Normalize(sessionId);
        }

        public void Deactivate()
        {
            ActiveSessionId = string.Empty;
        }

        public bool IsCurrent(string sessionId)
        {
            string value = Normalize(sessionId);
            return string.IsNullOrEmpty(value)
                || string.IsNullOrEmpty(ActiveSessionId)
                || string.Equals(value, ActiveSessionId, StringComparison.Ordinal);
        }

        public bool IsTriggerRegistered(string sessionId)
        {
            string value = Normalize(sessionId);
            return !string.IsNullOrEmpty(value) && _triggerRegisteredSessions.Contains(value);
        }

        public void MarkTriggerRegistered(string sessionId)
        {
            string value = Normalize(sessionId);
            if (!string.IsNullOrEmpty(value)) _triggerRegisteredSessions.Add(value);
        }

        public bool ScheduleLineAction(
            string sessionId,
            int lineIndex,
            float offsetSeconds,
            string label,
            Action action,
            Action<string, string> onStale,
            out float executeAt)
        {
            executeAt = 0f;
            string value = Normalize(sessionId);
            if (string.IsNullOrEmpty(value) || lineIndex < 0 || action == null) return false;

            float safeOffset = Math.Max(0f, offsetSeconds);
            if (_lineStartTimes.TryGetValue(BuildLineKey(value, lineIndex), out float lineStartTime))
            {
                executeAt = lineStartTime + safeOffset;
                _scheduler.Schedule(executeAt, CreateGuardedAction(value, label, action, onStale));
                return true;
            }

            _pendingLineActions.Add(new PendingLineAction
            {
                SessionId = value,
                LineIndex = lineIndex,
                OffsetSeconds = safeOffset,
                Label = label,
                Action = action
            });
            return false;
        }

        public IReadOnlyList<ScheduledLineActionInfo> RecordLineStart(
            string sessionId,
            int lineIndex,
            float lineStartTime,
            Action<string, string> onStale)
        {
            string value = Normalize(sessionId);
            var scheduled = new List<ScheduledLineActionInfo>();
            if (string.IsNullOrEmpty(value) || lineIndex < 0) return scheduled;

            _lineStartTimes[BuildLineKey(value, lineIndex)] = lineStartTime;
            for (int i = 0; i < _pendingLineActions.Count;)
            {
                PendingLineAction pending = _pendingLineActions[i];
                if (pending.LineIndex != lineIndex
                    || !string.Equals(pending.SessionId, value, StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                float executeAt = lineStartTime + pending.OffsetSeconds;
                _scheduler.Schedule(
                    executeAt,
                    CreateGuardedAction(value, pending.Label, pending.Action, onStale));
                scheduled.Add(new ScheduledLineActionInfo(
                    pending.Label,
                    pending.OffsetSeconds,
                    executeAt));
                _pendingLineActions.RemoveAt(i);
            }

            return scheduled;
        }

        public SequenceTransientClearResult ClearTransientState()
        {
            var result = new SequenceTransientClearResult(
                _scheduler.Clear(),
                _pendingLineActions.Count,
                _lineStartTimes.Count,
                _triggerRegisteredSessions.Count);
            _pendingLineActions.Clear();
            _lineStartTimes.Clear();
            _triggerRegisteredSessions.Clear();
            return result;
        }

        public Action CreateGuardedAction(
            string sessionId,
            string label,
            Action action,
            Action<string, string> onStale)
        {
            string value = Normalize(sessionId);
            return () =>
            {
                if (!IsCurrent(value))
                {
                    onStale?.Invoke(value, label ?? string.Empty);
                    return;
                }

                action();
            };
        }

        private static string BuildLineKey(string sessionId, int lineIndex)
        {
            return sessionId + "\u001f" + lineIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string Normalize(string sessionId)
        {
            return (sessionId ?? string.Empty).Trim();
        }

        private sealed class PendingLineAction
        {
            public string SessionId;
            public int LineIndex;
            public float OffsetSeconds;
            public string Label;
            public Action Action;
        }
    }

    internal sealed class ScheduledLineActionInfo
    {
        public ScheduledLineActionInfo(string label, float offsetSeconds, float executeAt)
        {
            Label = label;
            OffsetSeconds = offsetSeconds;
            ExecuteAt = executeAt;
        }

        public string Label { get; }
        public float OffsetSeconds { get; }
        public float ExecuteAt { get; }
    }

    internal sealed class SequenceTransientClearResult
    {
        public SequenceTransientClearResult(
            int delayedActionCount,
            int pendingLineActionCount,
            int lineStartCount,
            int triggerSessionCount)
        {
            DelayedActionCount = delayedActionCount;
            PendingLineActionCount = pendingLineActionCount;
            LineStartCount = lineStartCount;
            TriggerSessionCount = triggerSessionCount;
        }

        public int DelayedActionCount { get; }
        public int PendingLineActionCount { get; }
        public int LineStartCount { get; }
        public int TriggerSessionCount { get; }
        public bool IsEmpty => DelayedActionCount <= 0
            && PendingLineActionCount <= 0
            && LineStartCount <= 0
            && TriggerSessionCount <= 0;
    }
}
