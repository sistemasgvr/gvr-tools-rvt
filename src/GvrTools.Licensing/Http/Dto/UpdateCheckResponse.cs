using System.Runtime.Serialization;

namespace GvrTools.Licensing.Http.Dto
{
    [DataContract]
    public sealed class UpdateCheckResponse
    {
        [DataMember]
        public bool UpdateAvailable { get; set; }

        [DataMember]
        public string LatestVersion { get; set; }

        [DataMember]
        public string DownloadUrl { get; set; }

        [DataMember]
        public string ReleaseNotes { get; set; }
    }
}
