using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class HeartbeatRequest
    {
        [DataMember]
        public string DeviceFingerprint { get; set; }
    }

    [DataContract]
    public sealed class HeartbeatResponse
    {
        /// <summary>JWT renovado — reemplazar el AccessToken local.</summary>
        [DataMember]
        public string AccessToken { get; set; }

        [DataMember]
        public string EntitlementJson { get; set; }

        [DataMember]
        public string EntitlementSignatureBase64 { get; set; }
    }
}
