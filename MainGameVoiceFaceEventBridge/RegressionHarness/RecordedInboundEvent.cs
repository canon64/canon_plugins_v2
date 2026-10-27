using System;
using System.Runtime.Serialization;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    [DataContract]
    internal sealed class RecordedInboundEvent
    {
        [DataMember(Order = 1)] public int SchemaVersion = 1;
        [DataMember(Order = 2)] public long SequenceNo;
        [DataMember(Order = 3)] public long OffsetMs;
        [DataMember(Order = 4)] public string AnonymizedJson = string.Empty;
        [DataMember(Order = 5)] public string PayloadSha256 = string.Empty;
    }
}
