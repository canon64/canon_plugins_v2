using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge.Audio
{
    internal sealed class AudioSequenceState
    {
        private readonly object _sync = new object();
        private readonly Queue<ExternalVoicePlaybackItem> _queue =
            new Queue<ExternalVoicePlaybackItem>();
        private ExternalVoicePlaybackItem _pending;

        public string SessionId { get; private set; } = string.Empty;

        public bool HasQueuedOrPending
        {
            get
            {
                lock (_sync) return _queue.Count > 0 || _pending != null;
            }
        }

        public bool HasPending
        {
            get
            {
                lock (_sync) return _pending != null;
            }
        }

        public int Begin(IList<ExternalVoicePlaybackItem> items, string sessionId)
        {
            Clear();
            SessionId = sessionId ?? string.Empty;
            if (items == null) return 0;

            int accepted = 0;
            lock (_sync)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ExternalVoicePlaybackItem item = items[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                    if (item.Index <= 0) item.Index = i + 1;
                    item.Total = items.Count;
                    item.SequencePosition = i + 1;
                    _queue.Enqueue(item);
                    accepted++;
                }
            }
            return accepted;
        }

        public int Append(
            IList<ExternalVoicePlaybackItem> items,
            string sessionId,
            out string activeSessionId)
        {
            string value = sessionId ?? string.Empty;
            activeSessionId = SessionId;
            if (!string.IsNullOrEmpty(SessionId)
                && !string.Equals(SessionId, value, StringComparison.Ordinal)) return -1;
            if (string.IsNullOrEmpty(SessionId)) SessionId = value;
            activeSessionId = SessionId;
            if (items == null) return 0;

            int accepted = 0;
            lock (_sync)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ExternalVoicePlaybackItem item = items[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                    if (item.SequencePosition <= 0)
                    {
                        item.SequencePosition = item.Index > 0 ? item.Index : accepted + 1;
                    }
                    if (item.Index <= 0) item.Index = item.SequencePosition;
                    if (item.Total <= 0) item.Total = Math.Max(item.Index, item.SequencePosition);
                    _queue.Enqueue(item);
                    accepted++;
                }
            }
            return accepted;
        }

        public bool TryBeginNextLoad(out ExternalVoicePlaybackItem item)
        {
            lock (_sync)
            {
                if (_queue.Count <= 0)
                {
                    item = null;
                    return false;
                }
                item = _queue.Dequeue();
                _pending = item;
                return true;
            }
        }

        public void SetPending(ExternalVoicePlaybackItem item)
        {
            lock (_sync) _pending = item;
        }

        public ExternalVoicePlaybackItem TakePending()
        {
            lock (_sync)
            {
                ExternalVoicePlaybackItem item = _pending;
                _pending = null;
                return item;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _queue.Clear();
                _pending = null;
                SessionId = string.Empty;
            }
        }
    }
}
