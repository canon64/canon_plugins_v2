using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge.Scheduling
{
    internal sealed class StableActionScheduler
    {
        private sealed class Entry
        {
            internal long SequenceNo;
            internal float DueTime;
            internal Action Action;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private long _nextSequenceNo;

        internal int Count => _entries.Count;

        internal long Schedule(float dueTime, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            long sequenceNo = ++_nextSequenceNo;
            _entries.Add(new Entry { SequenceNo = sequenceNo, DueTime = dueTime, Action = action });
            return sequenceNo;
        }

        internal int DrainDue(float now, Action<Exception> onError)
        {
            if (_entries.Count <= 0) return 0;
            var due = new List<Entry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (now < entry.DueTime) continue;
                due.Add(entry);
                _entries.RemoveAt(i);
            }

            due.Sort((a, b) =>
            {
                int byTime = a.DueTime.CompareTo(b.DueTime);
                return byTime != 0 ? byTime : a.SequenceNo.CompareTo(b.SequenceNo);
            });

            foreach (Entry entry in due)
            {
                try { entry.Action(); }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
            return due.Count;
        }

        internal int Clear()
        {
            int count = _entries.Count;
            _entries.Clear();
            return count;
        }
    }
}
