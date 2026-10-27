using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge.ResponseText
{
    internal sealed class ResponseTextCommandQueue
    {
        private readonly Queue<ExternalVoiceFaceCommand> _commands =
            new Queue<ExternalVoiceFaceCommand>();

        public int Count => _commands.Count;

        public int Enqueue(ExternalVoiceFaceCommand command, int capacity)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            int safeCapacity = Math.Max(1, capacity);
            int dropped = 0;
            while (_commands.Count >= safeCapacity)
            {
                _commands.Dequeue();
                dropped++;
            }

            _commands.Enqueue(command);
            return dropped;
        }

        public bool TryDequeue(out ExternalVoiceFaceCommand command)
        {
            if (_commands.Count <= 0)
            {
                command = null;
                return false;
            }

            command = _commands.Dequeue();
            return true;
        }

        public int Clear()
        {
            int dropped = _commands.Count;
            _commands.Clear();
            return dropped;
        }
    }
}
