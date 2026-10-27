using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge.Inbound
{
    internal sealed class InboundCommandQueue
    {
        private readonly object _sync = new object();
        private readonly Queue<InboundCommandEnvelope> _queue = new Queue<InboundCommandEnvelope>();
        private long _nextSequenceNo;

        internal int Count
        {
            get
            {
                lock (_sync) return _queue.Count;
            }
        }

        internal InboundCommandEnvelope Enqueue(
            ExternalVoiceFaceCommand command,
            int capacity,
            DateTime receivedUtc,
            out int droppedCount)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            int safeCapacity = Math.Max(1, capacity);
            lock (_sync)
            {
                droppedCount = 0;
                while (_queue.Count >= safeCapacity)
                {
                    _queue.Dequeue();
                    droppedCount++;
                }

                var envelope = new InboundCommandEnvelope(++_nextSequenceNo, receivedUtc, command);
                _queue.Enqueue(envelope);
                return envelope;
            }
        }

        internal bool TryDequeue(out InboundCommandEnvelope envelope)
        {
            lock (_sync)
            {
                if (_queue.Count <= 0)
                {
                    envelope = null;
                    return false;
                }

                envelope = _queue.Dequeue();
                return true;
            }
        }

        internal int Clear()
        {
            lock (_sync)
            {
                int count = _queue.Count;
                _queue.Clear();
                return count;
            }
        }
    }
}
