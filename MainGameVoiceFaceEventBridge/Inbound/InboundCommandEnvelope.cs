using System;

namespace MainGameVoiceFaceEventBridge.Inbound
{
    internal sealed class InboundCommandEnvelope
    {
        internal InboundCommandEnvelope(long sequenceNo, DateTime receivedUtc, ExternalVoiceFaceCommand command)
        {
            SequenceNo = sequenceNo;
            ReceivedUtc = receivedUtc;
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        internal long SequenceNo { get; }
        internal DateTime ReceivedUtc { get; }
        internal ExternalVoiceFaceCommand Command { get; }
    }
}
