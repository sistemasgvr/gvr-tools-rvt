using System;
using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class UsageEventDto
    {
        [DataMember]
        public string DeviceFingerprint { get; set; }

        [DataMember]
        public Guid EventId { get; set; }

        [DataMember]
        public string FeatureCode { get; set; }

        [DataMember]
        public int Quantity { get; set; }

        [DataMember]
        public DateTimeOffset OccurredAtUtc { get; set; }
    }

    [DataContract]
    public sealed class UsageEventResponse
    {
        [DataMember]
        public int? Remaining { get; set; }
    }
}
