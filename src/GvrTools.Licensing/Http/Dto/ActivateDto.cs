using System;
using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class ActivateRequest
    {
        [DataMember]
        public string LicenseKey { get; set; }

        [DataMember]
        public string DeviceFingerprint { get; set; }

        [DataMember]
        public string DeviceName { get; set; }

        [DataMember]
        public string UserFullName { get; set; }

        [DataMember]
        public string UserEmail { get; set; }
    }

    [DataContract]
    public sealed class ActivateResponse
    {
        [DataMember]
        public string AccessToken { get; set; }

        [DataMember]
        public string EntitlementJson { get; set; }

        [DataMember]
        public string EntitlementSignatureBase64 { get; set; }
    }
}
