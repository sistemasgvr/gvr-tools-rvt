using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class DeactivateRequest
    {
        [DataMember]
        public string DeviceFingerprint { get; set; }
    }

    [DataContract]
    public sealed class DeactivateResponse
    {
        [DataMember]
        public bool Deactivated { get; set; }
    }
}
